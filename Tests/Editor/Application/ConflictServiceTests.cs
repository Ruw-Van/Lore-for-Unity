using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Conflicts;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Status;
using Lore.Unity.Infrastructure.Backend;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class ConflictServiceTests
    {
        private sealed class Journal : IConflictRecoveryJournal
        {
            public bool Applied = true;
            public Result VerifyAppliedMerge(OperationId id, RepositoryId repo) => Applied ? Result.Success() :
                Result.Failure(new LoreError(ErrorCode.ValidationFailed, "No pending merge."));
            public Task<Result> BeginAsync(OperationId id, RepositoryId repo, string operation, CancellationToken token) =>
                throw new Exception("Must not create a second journal.");
            public Task<Result> LoreAppliedAsync(OperationId id, CancellationToken token) =>
                throw new Exception("Must not replace the existing journal.");
            public Task<Result> CompleteAsync(OperationId id, CancellationToken token) =>
                throw new Exception("Must not complete recovery automatically.");
        }
        private sealed class Guard : IWorkingCopyGuard
        {
            public int After;
            public Task<Result> ValidateBeforeWriteAsync(RepositoryId repo, CancellationToken token) =>
                Task.FromResult(Result.Success());
            public Task<Result> ValidateAfterWriteAsync(RepositoryId repo, CancellationToken token)
            { After++; return Task.FromResult(Result.Success()); }
        }
        private sealed class Backend : IConflictBackend
        {
            public IReadOnlyList<RepositoryPath> Paths;
            public bool Fail;
            public Task<Result> ChooseVersionAsync(RepositoryId repo, IReadOnlyList<RepositoryPath> paths,
                ConflictChoice choice, CancellationToken token)
            {
                Paths = paths;
                return Task.FromResult(Fail ? Result.Failure(new LoreError(ErrorCode.Unknown, "Missing completion.")) :
                    Result.Success());
            }
        }
        private sealed class Status : ISerializedStatusBackend
        {
            private readonly Backend _backend;
            public Status(Backend backend) { _backend = backend; }
            public Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId repo,
                IReadOnlyList<RepositoryPath> paths, CancellationToken token) =>
                ReadUnderLeaseAsync(repo, paths, token);
            public Task<Result<IReadOnlyList<FileStatusEntry>>> ReadUnderLeaseAsync(RepositoryId repo,
                IReadOnlyList<RepositoryPath> paths, CancellationToken token)
            {
                var state = new FileStatus(WorkingState.Modified, StageState.Staged, LockState.Unknown,
                    _backend.Paths == null ? ConflictState.Conflicted : ConflictState.Resolved, RemoteState.Unknown);
                IReadOnlyList<FileStatusEntry> entries = new[]
                {
                    new FileStatusEntry(new RepositoryPath("Assets/a.prefab"), state),
                    new FileStatusEntry(new RepositoryPath("Assets/a.prefab.meta"), state)
                };
                return Task.FromResult(Result<IReadOnlyList<FileStatusEntry>>.Success(entries));
            }
        }

        [Test]
        public void ChoosesBothConflictedAssetAndMetaWithoutCompletingRecovery()
        {
            var backend = new Backend();
            var guard = new Guard();
            var journal = new Journal();
            var service = new ConflictService(backend, new Status(backend), new RepositoryOperationGate(), guard, journal);
            var id = new OperationId(Guid.NewGuid().ToString("N"));
            var repo = new RepositoryId(new string('a', 32));
            var result = service.ChooseAsync(id, repo, new RepositoryPath("Assets/a.prefab"),
                ConflictChoice.Mine, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Result.IsSuccess, Is.True);
            Assert.That(backend.Paths.Count, Is.EqualTo(2));
            Assert.That(guard.After, Is.EqualTo(1));
            journal.Applied = false;
            Assert.That(service.ChooseAsync(id, repo, new RepositoryPath("Assets/a.prefab"),
                ConflictChoice.Theirs, CancellationToken.None).Result.Result.IsFailure, Is.True);
        }

        [Test]
        public void AmbiguousCliFailureStillRefreshesUnityAndRetainsRecovery()
        {
            var backend = new Backend { Fail = true };
            var guard = new Guard();
            var service = new ConflictService(backend, new Status(backend), new RepositoryOperationGate(), guard,
                new Journal());
            var outcome = service.ChooseAsync(new OperationId(Guid.NewGuid().ToString("N")),
                new RepositoryId(new string('a', 32)), new RepositoryPath("Assets/a.prefab"),
                ConflictChoice.Theirs, CancellationToken.None).Result;
            Assert.That(outcome.Result.IsFailure, Is.True);
            Assert.That(outcome.LoreApplied, Is.True);
            Assert.That(guard.After, Is.EqualTo(1));
        }
    }
}
