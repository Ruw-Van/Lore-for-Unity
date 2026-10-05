using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Operations;
using Lore.Unity.Application.CheckIn;
using Lore.Unity.Application.Queries;
using Lore.Unity.Application.Runtime;
using Lore.Unity.Application.Status;
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
