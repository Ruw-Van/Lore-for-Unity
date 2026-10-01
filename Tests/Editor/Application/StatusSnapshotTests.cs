using System;
using Lore.Unity.Application.Status;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.Backend;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Identifiers;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class StatusSnapshotTests
    {
        private sealed class DelayedStatus : IStatusBackend, IBackendSet
        {
            public IRepositoryBackend Repository => null;
            public IStatusBackend Status => this;
            public readonly TaskCompletionSource<Result<IReadOnlyList<FileStatusEntry>>> First =
                new TaskCompletionSource<Result<IReadOnlyList<FileStatusEntry>>>();
            private int _calls;
            public Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId id,
                IReadOnlyList<RepositoryPath> paths, CancellationToken token)
            {
                if (Interlocked.Increment(ref _calls) == 1) return First.Task;
                return Task.FromResult(Result<IReadOnlyList<FileStatusEntry>>.Success(new FileStatusEntry[0]));
            }
        }

        [Test]
        public void ReaderDiscardsOutOfOrderBackendResult()
        {
            var backend = new DelayedStatus();
            var session = new BackendSession(new BackendResolver(new AvailableCapabilities(backend, null)),
                backend, null);
            var store = new StatusStore();
            var reader = new StatusReader(session, store);
            var old = reader.RefreshRepositoryAsync(new RepositoryId("repo"), CancellationToken.None);
            var latest = reader.RefreshRepositoryAsync(new RepositoryId("repo"), CancellationToken.None);
            Assert.That(latest.GetAwaiter().GetResult().IsSuccess, Is.True);
            backend.First.SetResult(Result<IReadOnlyList<FileStatusEntry>>.Success(new FileStatusEntry[0]));
            Assert.That(old.GetAwaiter().GetResult().IsFailure, Is.True);
            Assert.That(store.Current.Generation, Is.EqualTo(2L));
        }

        [Test]
        public void OlderRefreshCannotOverwriteNewSnapshot()
        {
            var store = new StatusStore();
            var older = store.BeginRefresh();
            var newer = store.BeginRefresh();
            var id = new RepositoryId("repo");
            var entries = new Lore.Unity.Application.Backend.FileStatusEntry[0];
            Assert.That(store.TryPublish(new StatusSnapshot(id, newer, DateTime.UtcNow, entries)), Is.True);
            Assert.That(store.TryPublish(new StatusSnapshot(id, older, DateTime.UtcNow, entries)), Is.False);
            Assert.That(store.Current.Generation, Is.EqualTo(newer));
        }

        [Test]
        public void SnapshotRejectsDuplicatePaths()
        {
            var path = new Lore.Unity.Core.Paths.RepositoryPath("Assets/a.txt");
            var status = new Lore.Unity.Core.Status.FileStatus();
            var entries = new[] { new Lore.Unity.Application.Backend.FileStatusEntry(path, status),
                new Lore.Unity.Application.Backend.FileStatusEntry(path, status) };
            Assert.Throws<ArgumentException>(() => new StatusSnapshot(new RepositoryId("repo"), 1,
                DateTime.UtcNow, entries));
        }
    }
}
