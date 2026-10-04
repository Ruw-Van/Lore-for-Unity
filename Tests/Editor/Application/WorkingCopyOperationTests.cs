using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.Backend;
using Lore.Unity.Infrastructure.Recovery;
using Lore.Unity.Core.Paths;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class WorkingCopyOperationTests
    {
        private sealed class Guard : IWorkingCopyGuard
        {
            public bool Unsafe;
            public bool ImportFailed;
            public Task<Result> ValidateBeforeWriteAsync(RepositoryId id, CancellationToken token) =>
                Task.FromResult(Unsafe ? Fail() : Result.Success());
            public Task<Result> ValidateAfterWriteAsync(RepositoryId id, CancellationToken token) =>
                Task.FromResult(ImportFailed ? Fail() : Result.Success());
            private static Result Fail() => Result.Failure(new LoreError(ErrorCode.ValidationFailed, "Unity state is unsafe."));
        }

        private sealed class Journal : IRecoveryJournal
        {
            public int Boundaries;
            public Task<Result> BeginAsync(OperationId id, RepositoryId repository, string operation, CancellationToken token)
            { Boundaries++; return Task.FromResult(Result.Success()); }
            public Task<Result> LoreAppliedAsync(OperationId id, CancellationToken token)
            { Boundaries++; return Task.FromResult(Result.Success()); }
            public Task<Result> CompleteAsync(OperationId id, CancellationToken token)
            { Boundaries++; return Task.FromResult(Result.Success()); }
        }

        private sealed class Verifier : IWorkingCopyStatusVerifier
        {
            public bool Fail;
            public int Calls;
            public Task<Result> VerifyUnderLeaseAsync(RepositoryId id, CancellationToken token)
            {
                Calls++;
                return Task.FromResult(Fail ? Result.Failure(new LoreError(ErrorCode.ValidationFailed,
                    "Status unavailable.")) : Result.Success());
            }
        }

        [Test]
        public void PreflightStopsLoreWriteBeforeJournal()
        {
            var journal = new Journal();
            var writes = 0;
            var operation = new WorkingCopyOperation(new RepositoryOperationGate(), new Guard { Unsafe = true },
                journal, new Verifier());
            var outcome = operation.ExecuteAsync(new RepositoryId("repo"), "Sync", token =>
            { writes++; return Task.FromResult(Result.Success()); }, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(outcome.Result.IsFailure, Is.True);
            Assert.That(writes, Is.EqualTo(0));
            Assert.That(journal.Boundaries, Is.EqualTo(0));
        }

        [Test]
        public void LoreSuccessWithUnityFailureLeavesJournalIncomplete()
        {
            var journal = new Journal();
            var operation = new WorkingCopyOperation(new RepositoryOperationGate(), new Guard { ImportFailed = true },
                journal, new Verifier());
            var outcome = operation.ExecuteAsync(new RepositoryId("repo"), "BranchSwitch",
                token => Task.FromResult(Result.Success()), CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(outcome.LoreApplied, Is.True);
            Assert.That(outcome.Result.IsFailure, Is.True);
            Assert.That(journal.Boundaries, Is.EqualTo(2));
        }

        [Test]
        public void FailedLoreWriteRemainsPendingAcrossJournalRestart()
        {
            var path = Path.Combine(Path.GetTempPath(), "lore-operation-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var root = new AbsolutePath(path);
                var repository = new RepositoryId(new string('c', 32));
                var journal = new FileRecoveryJournal(root);
                var operation = new WorkingCopyOperation(new RepositoryOperationGate(), new Guard(), journal,
                    new Verifier());
                var result = operation.ExecuteAsync(repository, "Sync", token =>
                    Task.FromResult(Result.Failure(new LoreError(ErrorCode.Unknown, "Unknown write outcome."))),
                    CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(result.Result.IsFailure, Is.True);
                var pending = new FileRecoveryJournal(root).Pending().Value;
                Assert.That(pending.Count, Is.EqualTo(1));
                Assert.That(pending[0].Id, Is.EqualTo(result.Id));
                Assert.That(pending[0].LoreApplied, Is.False);
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        }

        [Test]
        public void LoreStatusFailureAfterUnityRefreshKeepsRecoveryPending()
        {
            var journal = new Journal();
            var verifier = new Verifier { Fail = true };
            var operation = new WorkingCopyOperation(new RepositoryOperationGate(), new Guard(), journal, verifier);
            var outcome = operation.ExecuteAsync(new RepositoryId("repo"), "Sync",
                token => Task.FromResult(Result.Success()), CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(verifier.Calls, Is.EqualTo(1));
            Assert.That(outcome.Result.IsFailure, Is.True);
            Assert.That(outcome.LoreApplied, Is.True);
            Assert.That(journal.Boundaries, Is.EqualTo(2));
        }
    }
}
