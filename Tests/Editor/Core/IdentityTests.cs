using System;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Repository;
using NUnit.Framework;

namespace Lore.Unity.Tests.Core
{
    public sealed class IdentityTests
    {
        [Test]
        public void IdentifiersAreTypeSensitiveAndOrdinal()
        {
            Assert.That(new RepositoryId("a"), Is.EqualTo(new RepositoryId("a")));
            Assert.That(new RepositoryId("a"), Is.Not.EqualTo(new BranchId("a")));
            Assert.That(new RepositoryId("a"), Is.Not.EqualTo(new RepositoryId("A")));
        }

        [Test]
        public void RevisionNumberUsesValueEquality()
        {
            Assert.That(new RevisionNumber(7), Is.EqualTo(new RevisionNumber(7)));
            Assert.Throws<ArgumentOutOfRangeException>(() => new RevisionNumber(-1));
        }

        [Test]
        public void RepositorySnapshotRequiresIdentityAndRoot()
        {
            Assert.Throws<ArgumentNullException>(() => new RepositorySnapshot(null,
                new AbsolutePath(System.IO.Path.GetTempPath()), null, null));
            Assert.Throws<ArgumentException>(() => new RepositorySnapshot(new RepositoryId("repo"),
                default(AbsolutePath), null, null));
        }
    }
}
