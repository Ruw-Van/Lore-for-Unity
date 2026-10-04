using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Infrastructure.LoreCli;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class CliWriteEventParserTests
    {
        [Test]
        public void CommitRequiresSuccessfulCompletionAndMatchingIdentity()
        {
            var id = new RepositoryId(new string('a', 32));
            var signature = new string('b', 64);
            var events = "{\"tagName\":\"revisionCommitRevision\",\"data\":{\"repository\":\"" +
                id.Value + "\",\"revision\":\"" + signature + "\"}}\n" +
                "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n";
            Assert.That(new CliWriteEventParser().Commit(id, events).Value.Value, Is.EqualTo(signature));
            Assert.That(new CliWriteEventParser().Commit(new RepositoryId("other"), events).IsFailure, Is.True);
        }

        [Test]
        public void NonzeroCompletionDoesNotBecomeSuccess()
        {
            var events = "{\"tagName\":\"complete\",\"data\":{\"status\":4}}\n";
            Assert.That(new CliWriteEventParser().Verify(events).Error.Code, Is.EqualTo(ErrorCode.Unknown));
            Assert.That(new CliWriteEventParser().Verify("{\"tagName\":\"log\",\"data\":{}}\n").IsFailure, Is.True);
            Assert.That(new CliWriteEventParser().Verify(
                "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n" +
                "{\"tagName\":\"log\",\"data\":{}}\n").IsFailure, Is.True);
        }

        [Test]
        public void LocalCommitFollowedByRelayFailureIsNotReportedAsSuccessfulCommit()
        {
            // v0.10.0 can emit a local commit completion followed by a second,
            // failing relay completion even when --offline was supplied.
            var id = new RepositoryId(new string('a', 32));
            var events = "{\"tagName\":\"revisionCommitRevision\",\"data\":{\"repository\":\"" +
                id.Value + "\",\"revision\":\"" + new string('b', 64) + "\"}}\n" +
                "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n" +
                "{\"tagName\":\"log\",\"data\":{}}\n" +
                "{\"tagName\":\"complete\",\"data\":{\"status\":111}}\n";
            Assert.That(new CliWriteEventParser().Commit(id, events).IsFailure, Is.True);
            Assert.That(new CliWriteEventParser().LocalCommitCandidate(id, events).Value.Value,
                Is.EqualTo(new string('b', 64)));
            Assert.That(new CliWriteEventParser().LocalCommitCandidate(new RepositoryId(new string('c', 32)), events)
                .IsFailure, Is.True);
        }

        [Test]
        public void SyncRequiresMatchingRepositoryTarget()
        {
            var id = new RepositoryId(new string('a', 32));
            var target = "{\"tagName\":\"revisionSyncTarget\",\"data\":{\"repository\":\"" +
                id.Value + "\",\"targetRevision\":\"" + new string('b', 64) + "\"}}\n";
            var done = "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n";
            Assert.That(new CliWriteEventParser().Sync(id, target + done).IsSuccess, Is.True);
            Assert.That(new CliWriteEventParser().Sync(new RepositoryId(new string('c', 32)), target + done).IsFailure, Is.True);
            Assert.That(new CliWriteEventParser().Sync(id, done).IsFailure, Is.True);
        }

        [Test]
        public void BranchSwitchRequiresEndForRequestedBranch()
        {
            var begin = "{\"tagName\":\"branchSwitchBegin\",\"data\":{}}\n";
            var end = "{\"tagName\":\"branchSwitchEnd\",\"data\":{\"branch\":{\"name\":\"main\"}}}\n";
            var done = "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n";
            Assert.That(new CliWriteEventParser().BranchSwitch(new BranchName("main"), begin + end + done).IsSuccess, Is.True);
            Assert.That(new CliWriteEventParser().BranchSwitch(new BranchName("other"), begin + end + done).IsFailure, Is.True);
            Assert.That(new CliWriteEventParser().BranchSwitch(new BranchName("main"), begin + done).IsFailure, Is.True);
        }
    }
}
