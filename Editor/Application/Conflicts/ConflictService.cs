using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Operations;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Status;

namespace Lore.Unity.Application.Conflicts
{
    // Resolves only files reported as conflicted by a fresh Lore status. The
    // existing recovery record is retained until a separate acknowledgement.
    public sealed class ConflictService
    {
        private readonly IConflictBackend _backend;
        private readonly ISerializedStatusBackend _status;
        private readonly IRepositoryOperationGate _gate;
        private readonly IWorkingCopyGuard _guard;
        private readonly IConflictRecoveryJournal _journal;

        public ConflictService(IConflictBackend backend, ISerializedStatusBackend status,
            IRepositoryOperationGate gate, IWorkingCopyGuard guard, IConflictRecoveryJournal journal)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _status = status ?? throw new ArgumentNullException(nameof(status));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
        }

        public async Task<WriteOutcome> ChooseAsync(OperationId recoveryId, RepositoryId repository,
            RepositoryPath path, ConflictChoice choice, CancellationToken token)
        {
            if (recoveryId == null || repository == null) throw new ArgumentNullException(
                recoveryId == null ? nameof(recoveryId) : nameof(repository));
            if (string.IsNullOrEmpty(path.Value)) throw new ArgumentException("Path required.", nameof(path));
            if (choice != ConflictChoice.Mine && choice != ConflictChoice.Theirs)
                throw new ArgumentOutOfRangeException(nameof(choice));
            using (await _gate.AcquireAsync(repository, token))
            {
                var pending = _journal.VerifyAppliedMerge(recoveryId, repository);
                if (pending.IsFailure) return Failed(recoveryId, pending);
                var safe = await _guard.ValidateBeforeWriteAsync(repository, token);
                if (safe.IsFailure) return Failed(recoveryId, safe);
                var before = await _status.ReadUnderLeaseAsync(repository, Array.Empty<RepositoryPath>(), token);
                if (before.IsFailure) return Failed(recoveryId, Result.Failure(before.Error));
                var paths = new List<RepositoryPath>();
                foreach (var file in before.Value)
                    if (file.Path.Equals(path) && file.Status.Conflict == ConflictState.Conflicted)
                        paths.Add(path);
                if (paths.Count != 1) return Failed(recoveryId, Error("The selected file is not a native Lore conflict."));
                // Asset and .meta are one logical Unity unit. Apply one choice
                // to both if Lore marks both as conflicted.
                var meta = new RepositoryPath(path.Value + ".meta");
                foreach (var file in before.Value)
                    if (file.Path.Equals(meta) && file.Status.Conflict == ConflictState.Conflicted)
                        paths.Add(meta);
                Result applied;
                try { applied = await _backend.ChooseVersionAsync(repository, paths, choice, token); }
                catch (OperationCanceledException)
                {
                    applied = Error("Lore resolution outcome is unknown; refresh native status.");
                }
                // The CLI may have changed the working copy even when its final
                // event is missing. Always import and re-query under the lease.
                var unity = await _guard.ValidateAfterWriteAsync(repository, CancellationToken.None);
                if (unity.IsFailure) return Failed(recoveryId, unity, true);
                var after = await _status.ReadUnderLeaseAsync(repository, paths, CancellationToken.None);
                if (after.IsFailure) return Failed(recoveryId, Result.Failure(after.Error), true);
                if (applied.IsFailure) return Failed(recoveryId, applied, true);
                var checkedPaths = new HashSet<RepositoryPath>();
                foreach (var file in after.Value)
                    foreach (var target in paths)
                        if (file.Path.Equals(target))
                        {
                            checkedPaths.Add(target);
                            if (file.Status.Conflict == ConflictState.Conflicted ||
                                file.Status.Conflict == ConflictState.Unknown)
                                return Failed(recoveryId, Error("Lore still reports this file as conflicted."), true);
                        }
                if (checkedPaths.Count != paths.Count)
                    return Failed(recoveryId, Error("Resolved paths were not returned by Lore status."), true);
                return new WriteOutcome(recoveryId, Result.Success(), OperationState.Completed, true);
            }
        }

        private static WriteOutcome Failed(OperationId id, Result result, bool applied = false) =>
            new WriteOutcome(id, result, OperationState.Failed, applied);

        private static Result Error(string message) =>
            Result.Failure(new LoreError(ErrorCode.ValidationFailed, message));
    }
}
