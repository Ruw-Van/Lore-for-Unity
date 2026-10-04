using System;
using System.IO;
using Lore.Unity.Core.Paths;
using Lore.Unity.Integration.Assets;
using NUnit.Framework;

namespace Lore.Unity.Tests.Integration
{
    public sealed class UnityAssetPathMapperTests
    {
        private static UnityAssetPathMapper Nested()
        {
            var repo = Path.Combine(Path.GetTempPath(), "lore-mapper-" + Guid.NewGuid().ToString("N"));
            return new UnityAssetPathMapper(new AbsolutePath(Path.Combine(repo, "Game")), new AbsolutePath(repo));
        }

        [Test]
        public void ConvertsPathsWhenProjectIsNestedUnderRepository()
        {
            var mapper = Nested();
            Assert.That(mapper.AssetRootPrefix, Is.EqualTo("Game/Assets/"));
            var path = mapper.ToRepositoryPath(new UnityAssetPath("Assets/Scene.unity.meta"));
            Assert.That(path.Value.Value, Is.EqualTo("Game/Assets/Scene.unity.meta"));
            Assert.That(mapper.ToUnityPath(path.Value).Value.Value, Is.EqualTo("Assets/Scene.unity.meta"));
            Assert.That(mapper.ToUnityPath(new RepositoryPath("Game/ProjectSettings/ProjectSettings.asset"))
                .Value.Value, Is.EqualTo("ProjectSettings/ProjectSettings.asset"));
        }

        [Test]
        public void RejectsPathEscapeAndUnrelatedRepositoryFiles()
        {
            var mapper = Nested();
            Assert.That(mapper.ToRepositoryPath(new UnityAssetPath("Assets/../Secrets.txt")).IsFailure, Is.True);
            Assert.That(mapper.ToRepositoryPath(new UnityAssetPath("Assets/a//b")).IsFailure, Is.True);
            Assert.That(mapper.ToUnityPath(new RepositoryPath("Other/Assets/a.prefab")).IsFailure, Is.True);
            Assert.That(mapper.ToUnityPath(new RepositoryPath("Game/Library/cache")).IsFailure, Is.True);
        }

        [Test]
        public void RejectsProjectOutsideRepository()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lore-outside-" + Guid.NewGuid().ToString("N"));
            Assert.Throws<ArgumentException>(() => new UnityAssetPathMapper(
                new AbsolutePath(Path.Combine(dir, "other")), new AbsolutePath(Path.Combine(dir, "repo"))));
        }
    }
}
