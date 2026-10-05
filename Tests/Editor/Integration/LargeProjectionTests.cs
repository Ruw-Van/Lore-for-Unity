using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public sealed class LargeProjectionTests
    {
        private sealed class NoGuids : IUnityGuidLookup
        {
            public string AssetPathToGuid(UnityAssetPath path) => string.Empty;
            public string GuidToAssetPath(string guid) => string.Empty;
        }

        [Test]
        public void BuildsIndexedSnapshotsAtOneTenAndHundredThousandAssets()
        {
            var mapper = new UnityAssetPathMapper(new AbsolutePath(Path.Combine(Path.GetTempPath(), "lore-perf")),
                new AbsolutePath(Path.Combine(Path.GetTempPath(), "lore-perf")));
            foreach (var size in new[] { 1000, 10000, 100000 })
            {
                var entries = new List<FileStatusEntry>(size);
                var state = new FileStatus(WorkingState.Modified, StageState.Unstaged,
                    LockState.Unknown, ConflictState.None, RemoteState.Unknown);
                for (var i = 0; i < size; i++)
                    entries.Add(new FileStatusEntry(new RepositoryPath("Assets/Scenes/item" + i + ".prefab"), state));
                var status = new StatusSnapshot(new RepositoryId("repo"), 1, DateTime.UtcNow, entries);
                var timer = Stopwatch.StartNew();
                var result = UnityLogicalAssetIndex.Build(status, mapper, new NoGuids());
                timer.Stop();
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(result.Value.Assets.Count, Is.EqualTo(size));
                Assert.That(result.Value.ChangedWithinFolder(new UnityAssetPath("Assets/Scenes")), Is.EqualTo(size));
                Assert.That(result.Value.TryGet(new RepositoryPath("Assets/Scenes/item" + (size - 1) + ".prefab"),
                    out _), Is.True);
                Console.WriteLine("Projection assets=" + size + " elapsed_ms=" + timer.ElapsedMilliseconds);
            }
        }
    }
}
