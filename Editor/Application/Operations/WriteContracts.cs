using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Operations;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using System.Threading;
using System.Threading.Tasks;

namespace Lore.Unity.Application.Operations
{
    public interface IWorkingCopyGuard
    {
        // Integration must check unsaved scenes/prefabs on the Unity main thread.
        Task<Result> ValidateBeforeWriteAsync(RepositoryId repository, CancellationToken token);
        // Unity refresh/import/validation is required before a switch/sync is successful.
        Task<Result> ValidateAfterWriteAsync(RepositoryId repository, CancellationToken token);
    }

    public interface IRecoveryJournal
    {
        // Persist each boundary atomically; keep incomplete records for recovery.
        Task<Result> BeginAsync(OperationId id, RepositoryId repository, string operation, CancellationToken token);
        Task<Result> LoreAppliedAsync(OperationId id, CancellationToken token);
        Task<Result> CompleteAsync(OperationId id, CancellationToken token);
    }

    public sealed class CheckInPlan
    {
        public CheckInPlan(RepositoryId repository, IReadOnlyList<RepositoryPath> expandedPaths,
            string message, bool pushAfterCommit)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            if (expandedPaths == null || expandedPaths.Count == 0) throw new ArgumentException("Files required.", nameof(expandedPaths));
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Message required.", nameof(message));
            var unique = new HashSet<RepositoryPath>();
            foreach (var path in expandedPaths)
                if (string.IsNullOrEmpty(path.Value) || !unique.Add(path))
                    throw new ArgumentException("Invalid or duplicate path.", nameof(expandedPaths));
            Paths = new ReadOnlyCollection<RepositoryPath>(new List<RepositoryPath>(expandedPaths));
            Message = message;
            PushAfterCommit = pushAfterCommit;
        }
        public RepositoryId Repository { get; }
        public IReadOnlyList<RepositoryPath> Paths { get; }
        public string Message { get; }
        public bool PushAfterCommit { get; }
    }

    public sealed class CheckInOutcome
    {
        public CheckInOutcome(OperationId id, Result<RevisionSignature> commit,
            Result? push, OperationState state, bool commitOutcomeUnknown = false)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Commit = commit;
            Push = push;
            State = state;
            CommitOutcomeUnknown = commitOutcomeUnknown;
        }
        public OperationId Id { get; }
        public Result<RevisionSignature> Commit { get; }
        public Result? Push { get; }
        public OperationState State { get; }
        public bool IsCommitted => Commit.IsSuccess;
        public bool CommitOutcomeUnknown { get; }
    }

    public sealed class WriteOutcome
    {
        public WriteOutcome(OperationId id, Result result, OperationState state, bool loreApplied = false)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Result = result;
            State = state;
            LoreApplied = loreApplied;
        }
        public OperationId Id { get; }
        public Result Result { get; }
        public OperationState State { get; }
        public bool LoreApplied { get; }
    }

    internal static class WriteErrors
    {
        public static OperationId NewId() => new OperationId(Guid.NewGuid().ToString("N"));
        public static Result Failure(ErrorCode code, string message) => Result.Failure(new LoreError(code, message));
        public static Result<T> Failure<T>(ErrorCode code, string message) => Result<T>.Failure(new LoreError(code, message));
    }
}
