using System;
using System.IO;
using System.Threading;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Infrastructure.Recovery;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class FileRecoveryJournalTests
    {
        [Test]
        public void PendingBoundarySurvivesReconstructionAndBlocksSameRepository()
        {
            var path = Path.Combine(Path.GetTempPath(), "lore-journal-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var root = new AbsolutePath(path);
                var first = new FileRecoveryJournal(root);
                var id = new OperationId(Guid.NewGuid().ToString("N"));
                var repo = new RepositoryId(new string('a', 32));
                Assert.That(first.BeginAsync(id, repo, "Sync", CancellationToken.None).Result.IsSuccess, Is.True);
                Assert.That(first.LoreAppliedAsync(id, CancellationToken.None).Result.IsSuccess, Is.True);
                var reopened = new FileRecoveryJournal(root);
                Assert.That(reopened.Pending().Value.Count, Is.EqualTo(1));
                Assert.That(reopened.Pending().Value[0].LoreApplied, Is.True);
                Assert.That(reopened.BeginAsync(new OperationId(Guid.NewGuid().ToString("N")), repo,
                    "BranchSwitch", CancellationToken.None).Result.IsFailure, Is.True);
                Assert.That(reopened.CompleteAsync(id, CancellationToken.None).Result.IsSuccess, Is.True);
                Assert.That(new FileRecoveryJournal(root).Pending().Value.Count, Is.EqualTo(0));
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        }

        [Test]
        public void CorruptJournalBlocksNewWritesWithoutDeletingRecord()
        {
            var path = Path.Combine(Path.GetTempPath(), "lore-journal-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(path);
                var filename = new string('b', 32) + ".begin";
                File.WriteAllText(Path.Combine(path, filename), "incomplete");
                var journal = new FileRecoveryJournal(new AbsolutePath(path));
                Assert.That(journal.Pending().IsFailure, Is.True);
                Assert.That(journal.BeginAsync(new OperationId(Guid.NewGuid().ToString("N")),
                    new RepositoryId(new string('a', 32)), "Sync", CancellationToken.None).Result.IsFailure, Is.True);
                Assert.That(File.Exists(Path.Combine(path, filename)), Is.True);
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        }

        [Test]
        public void CompletionRequiresAppliedMarker()
        {
            var path = Path.Combine(Path.GetTempPath(), "lore-journal-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var journal = new FileRecoveryJournal(new AbsolutePath(path));
                var id = new OperationId(Guid.NewGuid().ToString("N"));
                var repo = new RepositoryId(new string('a', 32));
                Assert.That(journal.BeginAsync(id, repo, "BranchSwitch", CancellationToken.None).Result.IsSuccess, Is.True);
                Assert.That(journal.CompleteAsync(id, CancellationToken.None).Result.IsFailure, Is.True);
                Assert.That(journal.Pending().Value[0].LoreApplied, Is.False);
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        }

        [Test]
        public void MergeConflictRemainsPendingAcrossReopenUntilAcknowledged()
        {
            var path = Path.Combine(Path.GetTempPath(), "lore-merge-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                var journal = new FileRecoveryJournal(new AbsolutePath(path));
                var id = new OperationId(Guid.NewGuid().ToString("N"));
                var repo = new RepositoryId(new string('a', 32));
                Assert.That(journal.BeginAsync(id, repo, "BranchMerge", CancellationToken.None).Result.IsSuccess, Is.True);
                Assert.That(journal.LoreAppliedAsync(id, CancellationToken.None).Result.IsSuccess, Is.True);
                var reopened = new FileRecoveryJournal(new AbsolutePath(path));
                Assert.That(reopened.Pending().Value[0].Operation, Is.EqualTo("BranchMerge"));
                Assert.That(reopened.VerifyAppliedMerge(id, repo).IsSuccess, Is.True);
                Assert.That(reopened.VerifyAppliedMerge(new OperationId(Guid.NewGuid().ToString("N")), repo).IsFailure,
                    Is.True);
                Assert.That(reopened.CompleteAsync(id, CancellationToken.None).Result.IsSuccess, Is.True);
                Assert.That(reopened.Pending().Value.Count, Is.EqualTo(0));
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        }

        [Test]
        public void OtherProcessLockBlocksJournalReadsAndWrites()
        {
            var path = Path.Combine(Path.GetTempPath(), "lore-journal-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(path);
                var journal = new FileRecoveryJournal(new AbsolutePath(path));
                using (new FileStream(Path.Combine(path, ".journal.lock"), FileMode.OpenOrCreate,
                    FileAccess.ReadWrite, FileShare.None))
                {
                    Assert.That(journal.Pending().IsFailure, Is.True);
                    Assert.That(journal.BeginAsync(new OperationId(Guid.NewGuid().ToString("N")),
                        new RepositoryId(new string('a', 32)), "Sync", CancellationToken.None).Result.IsFailure, Is.True);
                }
                Assert.That(journal.Pending().IsSuccess, Is.True);
            }
            finally { if (Directory.Exists(path)) Directory.Delete(path, true); }
        }
    }
}
