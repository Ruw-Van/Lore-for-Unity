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

namespace Lore.Unity.Application.CheckIn
{
    public sealed class CheckInService
    {
        private readonly BackendSession _backends;
        private readonly IRepositoryOperationGate _gate;
        private readonly IWorkingCopyGuard _guard;

        public CheckInService(BackendSession backends, IRepositoryOperationGate gate, IWorkingCopyGuard guard)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        }

        public async Task<CheckInOutcome> ExecuteAsync(CheckInPlan plan, CancellationToken token)
        {
            if (plan == null) throw new ArgumentNullException(nameof(plan));
            var id = WriteErrors.NewId();
            // Resolve every backend before any side effect. Never retry against a second backend.
            var revision = _backends.ResolveRevision();
            var status = _backends.ResolveStatus();
            var push = plan.PushAfterCommit ? _backends.ResolvePush() : default(Result<IPushBackend>);
            if (revision.IsFailure || status.IsFailure || (plan.PushAfterCommit && push.IsFailure))
                return Failed(id, revision.IsFailure ? revision.Error : status.IsFailure ? status.Error : push.Error);

            using (await _gate.AcquireAsync(plan.Repository, token))
            {
                var validated = await _guard.ValidateBeforeWriteAsync(plan.Repository, token);
                if (validated.IsFailure) return Failed(id, validated.Error);
                // Commit includes all staged paths, not just paths passed to StageAsync.
                // Scan the entire repository before writing and reject unrelated staged files.
                var before = await ReadUnderLease(status.Value, plan.Repository,
                    Array.Empty<RepositoryPath>(), token);
                if (before.IsFailure) return Failed(id, before.Error);
                var selected = new HashSet<RepositoryPath>(plan.Paths);
                var changes = new List<RepositoryPath>();
                foreach (var entry in before.Value)
                {
                    if (entry.Status.Stage != StageState.Unstaged)
                        return Failed(id, new LoreError(ErrorCode.ValidationFailed,
                            "Existing staged changes must be resolved before Check In."));
                    if (selected.Contains(entry.Path) && entry.Status.Working != WorkingState.Unchanged)
                        changes.Add(entry.Path);
                }
                if (changes.Count != selected.Count)
                    return Failed(id, new LoreError(ErrorCode.ValidationFailed,
                        "Every selected path must have a detected change."));
                if (changes.Count == 0)
                    return Failed(id, new LoreError(ErrorCode.ValidationFailed, "No changes selected for Check In."));

                Result staged;
                try { staged = await revision.Value.StageAsync(plan.Repository, changes, token); }
                catch (OperationCanceledException)
                {
                    return new CheckInOutcome(id, WriteErrors.Failure<RevisionSignature>(ErrorCode.Cancelled,
                        "Stage may have changed; re-query Lore before retry."), null, OperationState.Cancelled);
                }
                if (staged.IsFailure) return Failed(id, staged.Error);
                Result<IReadOnlyList<FileStatusEntry>> after;
                try { after = await ReadUnderLease(status.Value, plan.Repository, changes, token); }
                catch (OperationCanceledException)
                {
                    return new CheckInOutcome(id, WriteErrors.Failure<RevisionSignature>(ErrorCode.Cancelled,
                        "Stage was applied; verification was cancelled."), null, OperationState.Cancelled);
                }
                if (after.IsFailure) return Failed(id, after.Error);
                var verified = new HashSet<RepositoryPath>();
                foreach (var entry in after.Value)
                {
                    if (!selected.Contains(entry.Path) || entry.Status.Stage != StageState.Staged ||
                        !verified.Add(entry.Path))
                        return Failed(id, new LoreError(ErrorCode.ValidationFailed,
                            "Stage verification failed; staged changes were not rolled back."));
                }
                foreach (var path in changes)
                    if (!verified.Contains(path))
                        return Failed(id, new LoreError(ErrorCode.ValidationFailed,
                            "Stage verification failed; staged changes were not rolled back."));

                Result<RevisionSignature> committed;
                try { committed = await revision.Value.CreateAsync(plan.Repository, plan.Message, token); }
                catch (OperationCanceledException)
                {
                    return new CheckInOutcome(id, WriteErrors.Failure<RevisionSignature>(ErrorCode.Unknown,
                        "Commit outcome is unknown; re-query Lore before retry."), null,
                        OperationState.Cancelled, true);
                }
                // A failed transport or CLI completion does not prove that Lore did
                // not commit locally. Never offer an automatic retry as a new commit.
                if (committed.IsFailure) return new CheckInOutcome(id, committed, null,
                    OperationState.Failed, true);
                if (!plan.PushAfterCommit)
                    return new CheckInOutcome(id, committed, null, OperationState.Completed);
                try
                {
                    var pushed = await push.Value.PushAsync(plan.Repository, token);
                    return new CheckInOutcome(id, committed, pushed,
                        pushed.IsSuccess ? OperationState.Completed : OperationState.Failed);
                }
                catch (OperationCanceledException)
                {
                    return new CheckInOutcome(id, committed,
                        WriteErrors.Failure(ErrorCode.Cancelled, "Push cancelled after commit."), OperationState.Cancelled);
                }
            }
        }

        private static Task<Result<IReadOnlyList<FileStatusEntry>>> ReadUnderLease(IStatusBackend status,
            RepositoryId repository, IReadOnlyList<RepositoryPath> paths, CancellationToken token)
        {
            var serialized = status as ISerializedStatusBackend;
            return serialized != null ? serialized.ReadUnderLeaseAsync(repository, paths, token) :
                status.ReadAsync(repository, paths, token);
        }

        private static CheckInOutcome Failed(OperationId id, LoreError error) =>
            new CheckInOutcome(id, Result<RevisionSignature>.Failure(error), null, OperationState.Failed);
    }
}
