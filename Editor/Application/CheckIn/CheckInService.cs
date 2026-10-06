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
            => await ExecuteCoreAsync(plan, token, false);

        // Explicit second phase: never stages or commits unselected paths.
        public Task<CheckInOutcome> CommitStagedAsync(CheckInPlan plan, CancellationToken token)
            => ExecuteCoreAsync(plan, token, true);

        private async Task<CheckInOutcome> ExecuteCoreAsync(CheckInPlan plan, CancellationToken token,
            bool requireStaged)
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
                var selected = new HashSet<RepositoryPath>();
                var logical = new HashSet<RepositoryPath>();
                foreach (var path in plan.Paths)
                {
                    if (!path.Value.StartsWith(plan.AssetRootPrefix, StringComparison.Ordinal))
                    {
                        selected.Add(path);
                        logical.Add(path);
                        continue;
                    }
                    var asset = path.Value.EndsWith(".meta", StringComparison.Ordinal)
                        ? new RepositoryPath(path.Value.Substring(0, path.Value.Length - ".meta".Length)) : path;
                    logical.Add(asset);
                    selected.Add(asset);
                    selected.Add(new RepositoryPath(asset.Value + ".meta"));
                }
                var changes = new List<RepositoryPath>();
                var found = new HashSet<RepositoryPath>();
                foreach (var entry in before.Value)
                {
                    if (entry.Status.Stage != StageState.Unstaged &&
                        (!requireStaged || !selected.Contains(entry.Path) &&
                         !StageSelectionService.IsAncestorOfSelection(entry.Path, selected)))
                        return Failed(id, new LoreError(ErrorCode.ValidationFailed,
                            "Unrelated staged changes must be resolved before Check In."));
                    if (selected.Contains(entry.Path) && (entry.Status.Working != WorkingState.Unchanged ||
                                                         entry.Status.Stage == StageState.Staged))
                    {
                        if (entry.Status.Conflict != ConflictState.None)
                            return Failed(id, new LoreError(ErrorCode.Conflict,
                                "Resolve selected Lore conflicts before Check In."));
                        changes.Add(entry.Path);
                        var asset = entry.Path.Value.StartsWith(plan.AssetRootPrefix, StringComparison.Ordinal) &&
                            entry.Path.Value.EndsWith(".meta", StringComparison.Ordinal)
                            ? new RepositoryPath(entry.Path.Value.Substring(0, entry.Path.Value.Length - ".meta".Length))
                            : entry.Path;
                        found.Add(asset);
                        if (requireStaged && entry.Status.Stage != StageState.Staged)
                            return Failed(id, new LoreError(ErrorCode.ValidationFailed,
                                "Every selected file must be staged before Check In."));
                    }
                }
                if (!found.SetEquals(logical))
                    return Failed(id, new LoreError(ErrorCode.ValidationFailed,
                        "Every selected logical asset or repository file must have a detected change."));
                if (changes.Count == 0)
                    return Failed(id, new LoreError(ErrorCode.ValidationFailed, "No changes selected for Check In."));

                if (!requireStaged)
                {
                    Result staged;
                    try { staged = await revision.Value.StageAsync(plan.Repository, changes, token); }
                    catch (OperationCanceledException)
                    {
                        return new CheckInOutcome(id, WriteErrors.Failure<RevisionSignature>(ErrorCode.Cancelled,
                            "Stage may have changed; re-query Lore before retry."), null, OperationState.Cancelled);
                    }
                    if (staged.IsFailure) return Failed(id, staged.Error);
                }
                Result<IReadOnlyList<FileStatusEntry>> after;
                if (requireStaged) after = before;
                else
                {
                    try { after = await ReadUnderLease(status.Value, plan.Repository, changes, token); }
                    catch (OperationCanceledException)
                    {
                        return new CheckInOutcome(id, WriteErrors.Failure<RevisionSignature>(ErrorCode.Cancelled,
                            "Stage was applied; verification was cancelled."), null, OperationState.Cancelled);
                    }
                    if (after.IsFailure) return Failed(id, after.Error);
                }
                var verified = new HashSet<RepositoryPath>();
                foreach (var entry in after.Value)
                {
                    if (!selected.Contains(entry.Path))
                    {
                        if (requireStaged) continue; // Full status includes unrelated, unstaged changes.
                        return Failed(id, new LoreError(ErrorCode.ValidationFailed,
                            "Stage verification failed; staged changes were not rolled back."));
                    }
                    if (entry.Status.Stage != StageState.Staged || !verified.Add(entry.Path))
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
                Result postCommit;
                try
                {
                    var refresh = await ReadUnderLease(status.Value, plan.Repository,
                        Array.Empty<RepositoryPath>(), CancellationToken.None);
                    postCommit = refresh.IsFailure ? Result.Failure(refresh.Error) : Result.Success();
                    if (refresh.IsSuccess)
                        foreach (var entry in refresh.Value)
                            if (selected.Contains(entry.Path) && entry.Status.Stage != StageState.Unstaged)
                            {
                                postCommit = WriteErrors.Failure(ErrorCode.ValidationFailed,
                                    "Commit succeeded, but selected files remain staged.");
                                break;
                            }
                }
                catch (OperationCanceledException)
                {
                    postCommit = WriteErrors.Failure(ErrorCode.Unknown,
                        "Commit succeeded, but status refresh was interrupted.");
                }
                if (postCommit.IsFailure)
                    return new CheckInOutcome(id, committed, null, OperationState.Failed,
                        postCommitStatus: postCommit);
                if (!plan.PushAfterCommit)
                    return new CheckInOutcome(id, committed, null, OperationState.Completed,
                        postCommitStatus: postCommit);
                try
                {
                    var pushed = await push.Value.PushAsync(plan.Repository, token);
                    return new CheckInOutcome(id, committed, pushed,
                        pushed.IsSuccess ? OperationState.Completed : OperationState.Failed,
                        postCommitStatus: postCommit);
                }
                catch (OperationCanceledException)
                {
                    return new CheckInOutcome(id, committed,
                        WriteErrors.Failure(ErrorCode.Cancelled, "Push cancelled after commit."), OperationState.Cancelled,
                        postCommitStatus: postCommit);
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
