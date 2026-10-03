using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.CheckIn;
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
    public sealed class CheckInServiceTests
    {
        private sealed class Fake : IWriteBackendSet, IRevisionBackend, IStatusBackend, IPushBackend
        {
            public IRepositoryBackend Repository => null;
            public IStatusBackend Status => this;
            public IRevisionBackend Revision => this;
            public IPushBackend Push => this;
            public ISyncBackend Sync => null;
            public IBranchBackend Branch => null;
            public ILockBackend Lock => null;
            public int Reads;
            public bool Staged;
            public bool PushFails;
            public bool VerifyFails;
            public bool CommitCancelled;
            public bool CommitFailed;
            public bool UnrelatedStaged;
            public int Commits;
            public Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId id,
                IReadOnlyList<RepositoryPath> paths, CancellationToken token)
            {
                Reads++;
                var status = new FileStatus(WorkingState.Modified,
                    Staged && !VerifyFails ? StageState.Staged : StageState.Unstaged,
                    LockState.Unknown, ConflictState.None, RemoteState.Unknown);
                var entries = new List<FileStatusEntry> { new FileStatusEntry(new RepositoryPath("Assets/a.txt"), status) };
                if (UnrelatedStaged && paths.Count == 0)
                    entries.Add(new FileStatusEntry(new RepositoryPath("Assets/other.txt"),
                        new FileStatus(WorkingState.Modified, StageState.Staged,
                            LockState.Unknown, ConflictState.None, RemoteState.Unknown)));
                return Task.FromResult(Result<IReadOnlyList<FileStatusEntry>>.Success(entries));
            }
            public Task<Result> StageAsync(RepositoryId id, IReadOnlyList<RepositoryPath> paths, CancellationToken token)
            { Staged = true; return Task.FromResult(Result.Success()); }
            public Task<Result<RevisionSignature>> CreateAsync(RepositoryId id, string message, CancellationToken token)
            {
                Commits++;
                if (CommitCancelled) throw new OperationCanceledException();
                if (CommitFailed) return Task.FromResult(Result<RevisionSignature>.Failure(
                    new LoreError(ErrorCode.Unknown, "CLI failure.")));
                return Task.FromResult(Result<RevisionSignature>.Success(new RevisionSignature("revision")));
            }
            public Task<Result> PushAsync(RepositoryId id, CancellationToken token) =>
                Task.FromResult(PushFails ? Result.Failure(new LoreError(ErrorCode.NetworkUnavailable, "Offline.")) : Result.Success());
        }

        private sealed class Guard : IWorkingCopyGuard
        {
            public Task<Result> ValidateBeforeWriteAsync(RepositoryId id, CancellationToken token) => Task.FromResult(Result.Success());
            public Task<Result> ValidateAfterWriteAsync(RepositoryId id, CancellationToken token) => Task.FromResult(Result.Success());
        }

        private static CheckInService Service(Fake backend)
        {
            var provider = new AvailableCapabilities(null, backend);
            return new CheckInService(new BackendSession(new BackendResolver(provider), null, backend),
                new RepositoryOperationGate(), new Guard());
        }

        [Test]
        public void CommitRemainsSuccessfulWhenPushFails()
        {
            var fake = new Fake { PushFails = true };
            var plan = new CheckInPlan(new RepositoryId("repo"), new[] { new RepositoryPath("Assets/a.txt") }, "Message", true);
            var outcome = Service(fake).ExecuteAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(outcome.IsCommitted, Is.True);
            Assert.That(outcome.Push.Value.Error.Code, Is.EqualTo(ErrorCode.NetworkUnavailable));
            Assert.That(fake.Reads, Is.EqualTo(2));
        }

        [Test]
        public void StageVerificationFailureNeverCommits()
        {
            var fake = new Fake { VerifyFails = true };
            var plan = new CheckInPlan(new RepositoryId("repo"), new[] { new RepositoryPath("Assets/a.txt") }, "Message", false);
            var outcome = Service(fake).ExecuteAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(outcome.IsCommitted, Is.False);
            Assert.That(outcome.Commit.Error.Code, Is.EqualTo(ErrorCode.ValidationFailed));
        }

        [Test]
        public void CheckInPlanRejectsDuplicatePaths()
        {
            Assert.Throws<ArgumentException>(() => new CheckInPlan(new RepositoryId("repo"),
                new[] { new RepositoryPath("Assets/a"), new RepositoryPath("Assets/a") }, "Message", false));
        }

        [Test]
        public void CancelledCommitIsExplicitlyUnknown()
        {
            var plan = new CheckInPlan(new RepositoryId("repo"), new[] { new RepositoryPath("Assets/a.txt") }, "Message", false);
            var outcome = Service(new Fake { CommitCancelled = true }).ExecuteAsync(plan, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(outcome.CommitOutcomeUnknown, Is.True);
            Assert.That(outcome.Commit.Error.Code, Is.EqualTo(ErrorCode.Unknown));
        }

        [Test]
        public void UnrelatedStagedChangesBlockCommit()
        {
            var fake = new Fake { UnrelatedStaged = true };
            var plan = new CheckInPlan(new RepositoryId("repo"), new[] { new RepositoryPath("Assets/a.txt") }, "Message", false);
            var outcome = Service(fake).ExecuteAsync(plan, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(outcome.IsCommitted, Is.False);
            Assert.That(fake.Staged, Is.False);
            Assert.That(fake.Commits, Is.EqualTo(0));
        }

        [Test]
        public void FailedCommitRequiresRequeryBeforeRetry()
        {
            var plan = new CheckInPlan(new RepositoryId("repo"), new[] { new RepositoryPath("Assets/a.txt") }, "Message", false);
            var outcome = Service(new Fake { CommitFailed = true }).ExecuteAsync(plan, CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(outcome.CommitOutcomeUnknown, Is.True);
            Assert.That(outcome.IsCommitted, Is.False);
        }
    }
}
