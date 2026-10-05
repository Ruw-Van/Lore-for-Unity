using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Operations;
using Lore.Unity.Application.CheckIn;
using Lore.Unity.Application.Diff;
using Lore.Unity.Application.Queries;
using Lore.Unity.Application.Runtime;
using Lore.Unity.Application.Status;
using Lore.Unity.Core.Status;
using Lore.Unity.Infrastructure.Recovery;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Integration.Assets;

namespace Lore.Unity.Integration.EditorLifecycle
{
    // Editor-facing controller; the UI never talks to a CLI or SDK adapter.
    public sealed class LoreWindowController
    {
        public RuntimeAvailability Availability => LoreBootstrap.Context.Availability;
        public RepositoryId Repository => LoreBootstrap.Repository;
        public StatusSnapshot Status => LoreBootstrap.Status;
        public UnityLogicalAssetIndex Assets => LoreBootstrap.Assets;
        public LoreError StatusError => LoreBootstrap.StatusError;
        public LoreError LockError => LoreBootstrap.LockError;
        public LoreError LastRefreshError { get; private set; }

        public Result<IReadOnlyList<PendingRecovery>> PendingRecovery() =>
            LoreBootstrap.Recovery != null ? LoreBootstrap.Recovery.Pending() :
                Result<IReadOnlyList<PendingRecovery>>.Failure(Unavailable());

        // Explicit acknowledgement only after the user has inspected the working copy
        // and resolved/aborted the native Lore merge independently.
        public async Task<Result> AcknowledgeRecoveryAsync(PendingRecovery pending, CancellationToken token)
        {
            if (pending == null || Repository == null || !pending.Repository.Equals(Repository))
                return Result.Failure(Unavailable());
            var journal = LoreBootstrap.Recovery;
            var refreshed = await RefreshAsync(token);
            if (refreshed.IsFailure) return Result.Failure(refreshed.Error);
            var current = PendingRecovery();
            if (current.IsFailure) return Result.Failure(current.Error);
            PendingRecovery found = null;
            foreach (var item in current.Value)
                if (item.Id.Equals(pending.Id)) found = item;
            if (found == null) return Result.Failure(new LoreError(ErrorCode.ValidationFailed,
                "Recovery record no longer exists."));
            foreach (var file in Status.Entries)
                if (file.Status.Conflict == ConflictState.Conflicted || file.Status.Conflict == ConflictState.Unknown)
                    return Result.Failure(new LoreError(ErrorCode.Conflict,
                        "Lore still reports conflicts; resolve or abort the merge first."));
            if (!found.LoreApplied)
            {
                var marked = await journal.LoreAppliedAsync(found.Id, token);
                if (marked.IsFailure) return marked;
            }
            return await journal.CompleteAsync(found.Id, token);
        }

        public Task<Result<UnityLogicalAssetIndex>> RefreshAsync(CancellationToken token) =>
            LoreBootstrap.RefreshStatusAsync(token);

        public Task<Result<IReadOnlyList<RevisionHistoryEntry>>> HistoryAsync(int limit, CancellationToken token)
        {
            var queries = LoreBootstrap.Queries;
            var repository = Repository;
            return queries != null && repository != null
                ? queries.HistoryAsync(repository, limit, token)
                : Task.FromResult(Result<IReadOnlyList<RevisionHistoryEntry>>.Failure(Unavailable()));
        }

        public Task<Result<IReadOnlyList<BranchName>>> BranchesAsync(CancellationToken token)
        {
            var queries = LoreBootstrap.Queries;
            var repository = Repository;
            return queries != null && repository != null
                ? queries.ListBranchesAsync(repository, token)
                : Task.FromResult(Result<IReadOnlyList<BranchName>>.Failure(Unavailable()));
        }

        public Task<Result<string>> DiffAsync(RepositoryPath path, DiffMode mode, CancellationToken token)
        {
            var service = LoreBootstrap.Diff;
            var repository = Repository;
            return service != null && repository != null
                ? service.ReadAsync(repository, path, mode, token)
                : Task.FromResult(Result<string>.Failure(Unavailable()));
        }

        public async Task<Result<WriteOutcome>> MergeAsync(BranchName source, CancellationToken token)
        {
            var repository = Repository;
            if (repository == null || source == null) return Result<WriteOutcome>.Failure(Unavailable());
            var writes = await LoreBootstrap.EnableEditingAsync(token);
            if (writes.IsFailure) return Result<WriteOutcome>.Failure(writes.Error);
            var outcome = await writes.Value.Merge.ExecuteAsync(repository, source, token);
            if (outcome.LoreApplied && !token.IsCancellationRequested)
                await RefreshAfterWriteAsync(token);
            return Result<WriteOutcome>.Success(outcome);
        }

        public async Task<Result<CheckInOutcome>> CheckInAsync(IReadOnlyList<RepositoryPath> paths,
            string message, bool push, CancellationToken token)
        {
            var repository = Repository;
            var prefix = LoreBootstrap.AssetRootPrefix;
            if (repository == null || prefix == null)
                return Result<CheckInOutcome>.Failure(Unavailable());
            CheckInPlan plan;
            try { plan = new CheckInPlan(repository, paths, message, push, prefix); }
            catch (ArgumentException)
            {
                return Result<CheckInOutcome>.Failure(new LoreError(ErrorCode.ValidationFailed,
                    "Select changed files and enter a Check In message."));
            }
            var writes = await LoreBootstrap.EnableEditingAsync(token);
            if (writes.IsFailure) return Result<CheckInOutcome>.Failure(writes.Error);
            var outcome = await writes.Value.CheckIn.ExecuteAsync(plan, token);
            if (outcome.IsCommitted && !token.IsCancellationRequested)
                await RefreshAfterWriteAsync(token);
            return Result<CheckInOutcome>.Success(outcome);
        }

        public async Task<Result<WriteOutcome>> SwitchAsync(BranchName branch, CancellationToken token)
        {
            if (Repository == null || branch == null) return Result<WriteOutcome>.Failure(Unavailable());
            var writes = await LoreBootstrap.EnableEditingAsync(token);
            if (writes.IsFailure) return Result<WriteOutcome>.Failure(writes.Error);
            var outcome = await writes.Value.Branch.SwitchAsync(Repository, branch, token);
            if (outcome.Result.IsSuccess && !token.IsCancellationRequested)
                await RefreshAfterWriteAsync(token);
            return Result<WriteOutcome>.Success(outcome);
        }

        private async Task RefreshAfterWriteAsync(CancellationToken token)
        {
            try
            {
                var refreshed = await LoreBootstrap.RefreshStatusAsync(token);
                LastRefreshError = refreshed.IsFailure ? refreshed.Error : null;
            }
            catch (OperationCanceledException)
            {
                LastRefreshError = new LoreError(ErrorCode.Cancelled,
                    "Status refresh was interrupted after the Lore write.");
            }
        }

        private static LoreError Unavailable() => new LoreError(ErrorCode.UnsupportedOperation,
            "A verified Lore repository and CLI are required.");
    }
}
