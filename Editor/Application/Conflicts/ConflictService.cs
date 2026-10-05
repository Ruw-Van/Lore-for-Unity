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
        private readonly IConflictTextWorkspace _workspace;
        private readonly IExternalMergeExecutor _external;
        private readonly IEditedConflictBackend _edited;

        public ConflictService(IConflictBackend backend, ISerializedStatusBackend status,
            IRepositoryOperationGate gate, IWorkingCopyGuard guard, IConflictRecoveryJournal journal,
            IConflictTextWorkspace workspace = null, IExternalMergeExecutor external = null,
            IEditedConflictBackend edited = null)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
            _status = status ?? throw new ArgumentNullException(nameof(status));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _workspace = workspace;
            _external = external;
            _edited = edited;
        }

        public async Task<Result<ConflictDraft>> PreviewAsync(OperationId recoveryId,
            RepositoryId repository, RepositoryPath path, CancellationToken token)
        {
            if (_workspace == null || recoveryId == null || repository == null)
                return Result<ConflictDraft>.Failure(new LoreError(ErrorCode.UnsupportedOperation,
                    "Text conflict preview is unavailable."));
            using (await _gate.AcquireAsync(repository, token))
            {
                var pending = _journal.VerifyAppliedMerge(recoveryId, repository);
                if (pending.IsFailure) return Result<ConflictDraft>.Failure(pending.Error);
                var status = await _status.ReadUnderLeaseAsync(repository, Array.Empty<RepositoryPath>(), token);
                if (status.IsFailure) return Result<ConflictDraft>.Failure(status.Error);
                if (!IsConflicted(status.Value, path)) return Result<ConflictDraft>.Failure(new LoreError(
                    ErrorCode.Conflict, "The selected path is not a native Lore conflict."));
                return await _workspace.ReadAsync(repository, path, token);
            }
        }

        public async Task<WriteOutcome> ResolveTextAsync(OperationId recoveryId, RepositoryId repository,
            RepositoryPath path, TextResolutionRequest request, CancellationToken token)
        {
            if (recoveryId == null || repository == null || request == null)
                throw new ArgumentNullException(recoveryId == null ? nameof(recoveryId) :
                    repository == null ? nameof(repository) : nameof(request));
            if (_workspace == null || _edited == null || _external == null)
                return Failed(recoveryId, Error("Text conflict resolution is unavailable."));
            using (await _gate.AcquireAsync(repository, token))
            {
                var pending = _journal.VerifyAppliedMerge(recoveryId, repository);
                if (pending.IsFailure) return Failed(recoveryId, pending);
                var safe = await _guard.ValidateBeforeWriteAsync(repository, token);
                if (safe.IsFailure) return Failed(recoveryId, safe);
                var before = await _status.ReadUnderLeaseAsync(repository, Array.Empty<RepositoryPath>(), token);
                if (before.IsFailure) return Failed(recoveryId, Result.Failure(before.Error));
                if (!IsConflicted(before.Value, path)) return Failed(recoveryId, Error("Lore no longer reports this conflict."));
                if (!path.Value.EndsWith(".meta", StringComparison.OrdinalIgnoreCase) &&
                    IsConflicted(before.Value, new RepositoryPath(path.Value + ".meta")))
                    return Failed(recoveryId, Error("Asset and .meta both conflict. Choose a version for the pair first."));
                var current = await _workspace.ReadAsync(repository, path, token);
                if (current.IsFailure) return Failed(recoveryId, Result.Failure(current.Error));
                var parsed = ConflictDocument.Parse(current.Value.Original);
                if (parsed.IsFailure) return Failed(recoveryId, Result.Failure(parsed.Error));
                Result<string> resolved;
                switch (request.Mode)
                {
                    case TextResolutionMode.Automatic:
                        var name = path.Value;
                        var yaml = (name.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                            name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) &&
                            current.Value.Original.StartsWith("%YAML 1.1", StringComparison.Ordinal) &&
                            current.Value.Original.Contains("--- !u!");
                        resolved = parsed.Value.AutoResolve(yaml);
                        break;
                    case TextResolutionMode.Manual:
                        if (request.Draft == null || !request.Draft.Repository.Equals(repository) ||
                            !request.Draft.Path.Equals(path) || request.Draft.Fingerprint != current.Value.Fingerprint ||
                            request.EditedText == null)
                            return Failed(recoveryId, Error("Text draft is stale. Reload the conflict."));
                        resolved = Result<string>.Success(request.EditedText);
                        break;
                    case TextResolutionMode.External:
                        if (request.Tool == null) return Failed(recoveryId, Error("Select a registered tool."));
                        try { resolved = await _external.MergeAsync(request.Tool, parsed.Value, token); }
                        catch (OperationCanceledException) { return Failed(recoveryId, Error("External tool cancelled.")); }
                        break;
                    default: return Failed(recoveryId, Error("Unsupported resolver."));
                }
                if (resolved.IsFailure) return Failed(recoveryId, Result.Failure(resolved.Error));
                if (resolved.Value.Length > 8 * 1024 * 1024 || resolved.Value.IndexOf('\0') >= 0 ||
                    ConflictDocument.ContainsMarkers(resolved.Value))
                    return Failed(recoveryId, Error("Resolution still contains markers or invalid text."));
                Result applied;
                try
                {
                    applied = await _workspace.WriteIfUnchangedAsync(current.Value, resolved.Value, token);
                    if (applied.IsSuccess) applied = await _edited.StageAndResolveAsync(repository, path, token);
                }
                catch (OperationCanceledException) { applied = Error("Lore resolution outcome is unknown; refresh status."); }
                // Once attempted, an error can be post-side-effect. Keep the journal
                // and import/re-query even if staging or native resolution failed.
                var unity = await _guard.ValidateAfterWriteAsync(repository, CancellationToken.None);
                if (unity.IsFailure) return Failed(recoveryId, unity, true);
                var after = await _status.ReadUnderLeaseAsync(repository, new[] { path }, CancellationToken.None);
                if (after.IsFailure) return Failed(recoveryId, Result.Failure(after.Error), true);
                if (applied.IsFailure) return Failed(recoveryId, applied, true);
                foreach (var file in after.Value)
                    if (file.Path.Equals(path) && file.Status.Conflict == ConflictState.Resolved &&
                        file.Status.Stage != StageState.Unstaged)
                        return new WriteOutcome(recoveryId, Result.Success(), OperationState.Completed, true);
                return Failed(recoveryId, Error("Lore did not confirm a staged, resolved file."), true);
            }
        }

        private static bool IsConflicted(IReadOnlyList<FileStatusEntry> entries, RepositoryPath path)
        {
            foreach (var file in entries)
                if (file.Path.Equals(path) && file.Status.Conflict == ConflictState.Conflicted) return true;
            return false;
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
