using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.Backend;
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

        [Test]
        public void PreflightStopsLoreWriteBeforeJournal()
        {
            var journal = new Journal();
            var writes = 0;
            var operation = new WorkingCopyOperation(new RepositoryOperationGate(), new Guard { Unsafe = true }, journal);
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
            var operation = new WorkingCopyOperation(new RepositoryOperationGate(), new Guard { ImportFailed = true }, journal);
            var outcome = operation.ExecuteAsync(new RepositoryId("repo"), "BranchSwitch",
                token => Task.FromResult(Result.Success()), CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(outcome.LoreApplied, Is.True);
            Assert.That(outcome.Result.IsFailure, Is.True);
            Assert.That(journal.Boundaries, Is.EqualTo(2));
        }
    }
}
