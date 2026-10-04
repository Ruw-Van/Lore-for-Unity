using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Status;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Status;
using Lore.Unity.Infrastructure.Backend;
using Lore.Unity.Integration.Assets;
using NUnit.Framework;

namespace Lore.Unity.Tests.Integration
{
    public sealed class UnityStatusProjectionTests
    {
        private sealed class Backend : IBackendSet, IStatusBackend
        {
            public IRepositoryBackend Repository => null;
            public IStatusBackend Status => this;
            public Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId repository,
                IReadOnlyList<RepositoryPath> paths, CancellationToken token)
            {
                IReadOnlyList<FileStatusEntry> result = new[] {
                    new FileStatusEntry(new RepositoryPath("Assets/a.prefab.meta"),
                        new FileStatus(WorkingState.Modified, StageState.Unstaged,
                            LockState.Unknown, ConflictState.None, RemoteState.Unknown)) };
                return Task.FromResult(Result<IReadOnlyList<FileStatusEntry>>.Success(result));
            }
        }

        private sealed class Guids : IUnityGuidLookup
        {
            public string AssetPathToGuid(UnityAssetPath path) => new string('a', 32);
            public string GuidToAssetPath(string guid) => "Assets/a.prefab";
        }

        [Test]
        public void StatusProjectionDiscardsDerivedIndexWhenStoreMovesAhead()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-projection-" + Guid.NewGuid().ToString("N"));
            var mapper = new UnityAssetPathMapper(new AbsolutePath(root), new AbsolutePath(root));
            var backend = new Backend();
            var session = new BackendSession(new BackendResolver(new AvailableCapabilities(null, backend)), null, backend);
            var store = new StatusStore();
            var reader = new StatusReader(session, store);
            var repository = new RepositoryId("repo");
            var projection = new UnityStatusProjection(reader, store, mapper, new Guids());
            var first = projection.RefreshAsync(repository, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(first.IsSuccess, Is.True);
            Assert.That(projection.Current.Assets[0].HasOnlyMetaChange, Is.True);
            var second = reader.RefreshRepositoryAsync(repository, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(second.IsSuccess, Is.True);
            Assert.That(projection.Current == null, Is.True);
        }
    }
}
