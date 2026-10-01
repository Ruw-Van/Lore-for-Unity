using System;
using Lore.Unity.Core.Paths;
using NUnit.Framework;

namespace Lore.Unity.Tests.Core
{
    public sealed class PathTests
    {
        [Test]
        public void RepositoryPathNormalizesSeparators()
        {
            var path = new RepositoryPath(@"Assets\Scenes\Main.unity");

            Assert.That(path.Value, Is.EqualTo("Assets/Scenes/Main.unity"));
        }

        [Test]
        public void UnityAssetPathRejectsBackslashes()
        {
            Assert.Throws<ArgumentException>(() => new UnityAssetPath(@"Assets\Main.prefab"));
        }

        [Test]
        public void AbsolutePathRejectsRelativePaths()
        {
            Assert.Throws<ArgumentException>(() => new AbsolutePath("Assets/Main.prefab"));
        }

        [Test]
        public void RepositoryPathRejectsAbsoluteAndTraversalPaths()
        {
            Assert.Throws<ArgumentException>(() => new RepositoryPath("../outside"));
            Assert.Throws<ArgumentException>(() => new RepositoryPath("Assets/../outside"));
            Assert.Throws<ArgumentException>(() => new RepositoryPath("/outside"));
            Assert.Throws<ArgumentException>(() => new RepositoryPath("C:\\outside"));
        }
    }
}
