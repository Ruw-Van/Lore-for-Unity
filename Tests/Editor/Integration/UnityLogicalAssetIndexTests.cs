using System;
using System.Collections.Generic;
using System.IO;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Status;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Status;
using Lore.Unity.Integration.Assets;
using NUnit.Framework;

namespace Lore.Unity.Tests.Integration
{
    public sealed class UnityLogicalAssetIndexTests
    {
        private sealed class Guids : IUnityGuidLookup
        {
            public bool Duplicate;
            public bool Invalid;
            public string AssetPathToGuid(UnityAssetPath path) => path.Value == "Assets/Scene.unity" ||
                (Duplicate && path.Value == "Assets/Other.prefab") ? Invalid ? "invalid" : new string('a', 32) : string.Empty;
            public string GuidToAssetPath(string guid) => "Assets/Scene.unity";
        }

        private static (UnityAssetPathMapper mapper, StatusSnapshot status) BuildInput(bool includeOther = false)
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-assets-" + Guid.NewGuid().ToString("N"));
            var mapper = new UnityAssetPathMapper(new AbsolutePath(root), new AbsolutePath(root));
            var state = new FileStatus(WorkingState.Modified, StageState.Unstaged,
                LockState.Unknown, ConflictState.None, RemoteState.Unknown);
            var entries = new List<FileStatusEntry>
            {
                new FileStatusEntry(new RepositoryPath("Assets/Scene.unity.meta"), state),
                new FileStatusEntry(new RepositoryPath("ProjectSettings/ProjectSettings.asset"), state),
                new FileStatusEntry(new RepositoryPath("Assets/Scenes/Nested.unity"), state)
            };
            if (includeOther) entries.Add(new FileStatusEntry(new RepositoryPath("Assets/Other.prefab"), state));
            return (mapper, new StatusSnapshot(new RepositoryId("repo"), 1, DateTime.UtcNow, entries));
        }

        [Test]
        public void MetaOnlyChangeRemainsVisibleAsLogicalAsset()
        {
            var input = BuildInput();
            var result = UnityLogicalAssetIndex.Build(input.status, input.mapper, new Guids());
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Assets.Count, Is.EqualTo(2));
            Assert.That(result.Value.RepositoryFiles.Count, Is.EqualTo(1));
            Assert.That(result.Value.TryGetGuid(new string('a', 32), out var scene), Is.True);
            Assert.That(scene.HasOnlyMetaChange, Is.True);
            Assert.That(scene.Meta.Path, Is.EqualTo(new RepositoryPath("Assets/Scene.unity.meta")));
            Assert.That(result.Value.TryGet(new RepositoryPath("Assets/Scene.unity.meta"), out var indexed), Is.True);
            Assert.That(indexed, Is.SameAs(scene));
        }

        [Test]
        public void FolderCountDoesNotDoubleCountMetaPair()
        {
            var input = BuildInput();
            var result = UnityLogicalAssetIndex.Build(input.status, input.mapper, new Guids());
            Assert.That(result.Value.ChangedWithinFolder(new UnityAssetPath("Assets")), Is.EqualTo(2));
            Assert.That(result.Value.ChangedWithinFolder(new UnityAssetPath("Assets/Scenes")), Is.EqualTo(1));
        }

        [Test]
        public void AmbiguousGuidRejectsWholeProjection()
        {
            var input = BuildInput(true);
            Assert.That(UnityLogicalAssetIndex.Build(input.status, input.mapper,
                new Guids { Duplicate = true }).IsFailure, Is.True);
            Assert.That(UnityLogicalAssetIndex.Build(input.status, input.mapper,
                new Guids { Invalid = true }).IsFailure, Is.True);
        }

        [Test]
        public void GuidSelectionUsesUnityPathButRejectsRepositoryFiles()
        {
            var input = BuildInput();
            var resolver = new UnityAssetSelectionResolver(new Guids(), input.mapper);
            Assert.That(resolver.ResolveGuid(new string('a', 32)).Value.Value, Is.EqualTo("Assets/Scene.unity"));
            Assert.That(resolver.ResolveGuid("not-a-guid").IsFailure, Is.True);
        }

        [Test]
        public void SelectionBuilderUsesNestedAssetRootForCheckIn()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-select-" + Guid.NewGuid().ToString("N"));
            var mapper = new UnityAssetPathMapper(new AbsolutePath(Path.Combine(root, "Game")),
                new AbsolutePath(root));
            var resolver = new UnityAssetSelectionResolver(new Guids(), mapper);
            var selection = new[] { new UnityAssetPath("Assets/Scene.unity"),
                new UnityAssetPath("Assets/Scene.unity.meta"),
                new UnityAssetPath("ProjectSettings/ProjectSettings.asset") };
            var plan = resolver.CreateCheckInPlan(new RepositoryId("repo"), selection, "Message", true);
            Assert.That(plan.IsSuccess, Is.True);
            Assert.That(plan.Value.Paths.Count, Is.EqualTo(2));
            Assert.That(plan.Value.AssetRootPrefix, Is.EqualTo("Game/Assets/"));
        }
    }
}
