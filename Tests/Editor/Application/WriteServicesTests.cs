using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Branches;
using Lore.Unity.Application.Locks;
using Lore.Unity.Application.Operations;
using Lore.Unity.Application.Push;
using Lore.Unity.Application.Sync;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.Backend;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class WriteServicesTests
    {
        private sealed class Backend : IWriteBackendSet, IPushBackend, ISyncBackend, IBranchBackend, ILockBackend
        {
            public IRepositoryBackend Repository => null;
            public IStatusBackend Status => null;
            public IRevisionBackend Revision => null;
            public IPushBackend Push => this;
            public ISyncBackend Sync => this;
            public IBranchBackend Branch => this;
            public ILockBackend Lock => this;
            public int PushCalls;
            public int SwitchCalls;
            public int SyncCalls;
            public int LockCalls;
            public bool CancelPush;
            public bool FailLock;
            public Task<Result> PushAsync(RepositoryId repository, CancellationToken token)
            {
                PushCalls++;
                if (CancelPush) throw new OperationCanceledException();
                return Task.FromResult(Result.Success());
            }
            public Task<Result> SyncAsync(RepositoryId repository, CancellationToken token)
            { SyncCalls++; return Task.FromResult(Result.Success()); }
            public Task<Result<IReadOnlyList<BranchName>>> ListAsync(RepositoryId repository, CancellationToken token)
            {
                IReadOnlyList<BranchName> branches = new[] { new BranchName("main") };
                return Task.FromResult(Result<IReadOnlyList<BranchName>>.Success(branches));
            }
            public Task<Result> SwitchAsync(RepositoryId repository, BranchName branch, CancellationToken token)
            { SwitchCalls++; return Task.FromResult(Result.Success()); }
            public Task<Result> AcquireAsync(RepositoryId repository, RepositoryPath path, CancellationToken token)
            {
                LockCalls++;
                return Task.FromResult(FailLock ? Result.Failure(new LoreError(ErrorCode.Locked, "Already locked.")) : Result.Success());
            }
            public Task<Result> ReleaseAsync(RepositoryId repository, RepositoryPath path, CancellationToken token) =>
                Task.FromResult(Result.Success());
        }

        private sealed class Guard : IWorkingCopyGuard
        {
            public bool Unsafe;
            public Task<Result> ValidateBeforeWriteAsync(RepositoryId id, CancellationToken token) =>
                Task.FromResult(Unsafe ? Result.Failure(new LoreError(ErrorCode.ValidationFailed, "Unsaved scene.")) : Result.Success());
            public Task<Result> ValidateAfterWriteAsync(RepositoryId id, CancellationToken token) => Task.FromResult(Result.Success());
        }

        private sealed class Journal : IRecoveryJournal
        {
            public int Boundaries;
            public Task<Result> BeginAsync(OperationId id, RepositoryId repo, string operation, CancellationToken token)
            { Boundaries++; return Task.FromResult(Result.Success()); }
            public Task<Result> LoreAppliedAsync(OperationId id, CancellationToken token)
            { Boundaries++; return Task.FromResult(Result.Success()); }
            public Task<Result> CompleteAsync(OperationId id, CancellationToken token)
            { Boundaries++; return Task.FromResult(Result.Success()); }
        }

        private sealed class Verifier : IWorkingCopyStatusVerifier
        {
            public Task<Result> VerifyUnderLeaseAsync(RepositoryId repo, CancellationToken token) =>
                Task.FromResult(Result.Success());
        }

        private static BackendSession Session(Backend backend) =>
            new BackendSession(new BackendResolver(new AvailableCapabilities(null, backend)), null, backend);

        [Test]
        public void CancelledPushCannotBeReportedAsRemoteSuccess()
        {
            var backend = new Backend { CancelPush = true };
            var result = new PushService(Session(backend), new RepositoryOperationGate())
                .ExecuteAsync(new RepositoryId("repo"), CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Result.IsFailure, Is.True);
            Assert.That(result.State, Is.EqualTo(Lore.Unity.Core.Operations.OperationState.Cancelled));
            Assert.That(backend.PushCalls, Is.EqualTo(1));
        }

        [Test]
        public void LockFailureRetainsLoreError()
        {
            var backend = new Backend { FailLock = true };
            var result = new LockService(Session(backend), new RepositoryOperationGate())
                .AcquireAsync(new RepositoryId("repo"), new RepositoryPath("Assets/a.prefab"), CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(result.Result.Error.Code, Is.EqualTo(ErrorCode.Locked));
        }

        [Test]
        public void UnsafeUnityStatePreventsSyncAndBranchSwitch()
        {
            var backend = new Backend();
            var gate = new RepositoryOperationGate();
            var journal = new Journal();
            var operation = new WorkingCopyOperation(gate, new Guard { Unsafe = true }, journal, new Verifier());
            var repo = new RepositoryId("repo");
            var sync = new SyncService(Session(backend), operation).ExecuteAsync(repo, CancellationToken.None)
                .GetAwaiter().GetResult();
            var branch = new BranchService(Session(backend), operation, gate)
                .SwitchAsync(repo, new BranchName("main"), CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(sync.Result.IsFailure, Is.True);
            Assert.That(branch.Result.IsFailure, Is.True);
            Assert.That(backend.SyncCalls + backend.SwitchCalls + journal.Boundaries, Is.EqualTo(0));
        }

        [Test]
        public void BranchListIsProvidedByChosenBackend()
        {
            var backend = new Backend();
            var gate = new RepositoryOperationGate();
            var operation = new WorkingCopyOperation(gate, new Guard(), new Journal(), new Verifier());
            var result = new BranchService(Session(backend), operation, gate)
                .ListAsync(new RepositoryId("repo"), CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Value[0].Value, Is.EqualTo("main"));
        }
    }
}
