using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.CheckIn;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Status;
using Lore.Unity.Infrastructure.Backend;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class StageSelectionServiceTests
    {
        private sealed class ProgressLog : IProgress<StageSelectionProgress>
        {
            public readonly List<StageSelectionProgress> Values = new List<StageSelectionProgress>();
            public void Report(StageSelectionProgress value) => Values.Add(value);
        }

        private sealed class CancellingProgress : IProgress<StageSelectionProgress>
        {
            private readonly CancellationTokenSource _source;
            public CancellingProgress(CancellationTokenSource source) { _source = source; }
            public void Report(StageSelectionProgress value)
            { if (value.Completed >= 50) _source.Cancel(); }
        }

        private sealed class Backend : IWriteBackendSet, IStatusBackend, IRevisionBackend
        {
            public IRepositoryBackend Repository => null;
            public IStatusBackend Status => this;
            public IRevisionBackend Revision => this;
            public IPushBackend Push => null;
            public ISyncBackend Sync => null;
            public IBranchBackend Branch => null;
            public ILockBackend Lock => null;
            public IMergeBackend Merge => null;
            public readonly HashSet<RepositoryPath> Staged = new HashSet<RepositoryPath>();
            public readonly List<IReadOnlyList<RepositoryPath>> Batches = new List<IReadOnlyList<RepositoryPath>>();
            public int Groups = 125;
            public int FailAt;
            public bool UnrelatedStaged;

            public Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId repository,
                IReadOnlyList<RepositoryPath> paths, CancellationToken token)
            {
                var list = new List<FileStatusEntry>();
                for (var i = 0; i < Groups; i++)
                    foreach (var suffix in new[] { ".asset", ".asset.meta" })
                    {
                        var path = new RepositoryPath("Assets/Item" + i.ToString("D5") + suffix);
                        list.Add(new FileStatusEntry(path, new FileStatus(WorkingState.Added,
                            Staged.Contains(path) ? StageState.Staged : StageState.Unstaged,
                            LockState.Unknown, ConflictState.None, RemoteState.Unknown)));
                    }
                if (UnrelatedStaged) list.Add(new FileStatusEntry(new RepositoryPath("ProjectSettings/Other.asset"),
                    new FileStatus(WorkingState.Modified, StageState.Staged,
                        LockState.Unknown, ConflictState.None, RemoteState.Unknown)));
                if (Staged.Count > 0) list.Add(new FileStatusEntry(new RepositoryPath("Assets"),
                    new FileStatus(WorkingState.Added, StageState.Staged,
                        LockState.Unknown, ConflictState.None, RemoteState.Unknown)));
                return Task.FromResult(Result<IReadOnlyList<FileStatusEntry>>.Success(list));
            }

            public Task<Result> StageAsync(RepositoryId repository, IReadOnlyList<RepositoryPath> paths,
                CancellationToken token)
            {
                Batches.Add(new List<RepositoryPath>(paths));
                if (Batches.Count == FailAt)
                {
                    Staged.Add(paths[0]); // Lore may have applied a partial batch before failing.
                    return Task.FromResult(Result.Failure(new LoreError(ErrorCode.Unknown, "Interrupted.")));
                }
                foreach (var path in paths) Staged.Add(path);
                return Task.FromResult(Result.Success());
            }

            public Task<Result<RevisionSignature>> CreateAsync(RepositoryId repository, string message,
                CancellationToken token) => throw new InvalidOperationException("Stage must not commit.");
        }

        private sealed class Guard : IWorkingCopyGuard
        {
            public Task<Result> ValidateBeforeWriteAsync(RepositoryId repository, CancellationToken token) =>
                Task.FromResult(Result.Success());
            public Task<Result> ValidateAfterWriteAsync(RepositoryId repository, CancellationToken token) =>
                Task.FromResult(Result.Success());
        }

        private static StageSelectionService Service(Backend backend)
        {
            var session = new BackendSession(new BackendResolver(new AvailableCapabilities(null, backend)),
                null, backend);
            return new StageSelectionService(session, new RepositoryOperationGate(), new Guard());
        }

        private static List<RepositoryPath> Selection(int count)
        {
            var paths = new List<RepositoryPath>();
            for (var i = 0; i < count; i++) paths.Add(new RepositoryPath("Assets/Item" + i.ToString("D5") + ".asset"));
            return paths;
        }

        [Test]
        public void StagesAssetMetaPairsInBoundedBatchesWithoutCommitting()
        {
            var backend = new Backend();
            var progress = new ProgressLog();
            var result = Service(backend).StageAsync(new RepositoryId("repo"), Selection(125), "Assets/",
                progress, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(backend.Batches.Count, Is.EqualTo(3));
            foreach (var batch in backend.Batches)
            {
                Assert.That(batch.Count <= 100, Is.True);
                Assert.That(batch.Count % 2, Is.EqualTo(0));
                for (var i = 0; i < batch.Count; i += 2)
                    Assert.That(batch[i + 1].Value, Is.EqualTo(batch[i].Value + ".meta"));
            }
            Assert.That(backend.Staged.Count, Is.EqualTo(250));
            Assert.That(progress.Values[progress.Values.Count - 1].Completed, Is.EqualTo(125));
        }

        [Test]
        public void PartialFailureIsRecoverableButNeverReportedAsComplete()
        {
            var backend = new Backend { FailAt = 2 };
            var service = Service(backend);
            var selected = Selection(125);
            var first = service.StageAsync(new RepositoryId("repo"), selected, "Assets/", null,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(first.IsFailure, Is.True);
            Assert.That(backend.Staged.Count > 0 && backend.Staged.Count < 250, Is.True);
            backend.FailAt = 0;
            var resumed = service.StageAsync(new RepositoryId("repo"), selected, "Assets/", null,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(resumed.IsSuccess, Is.True);
            Assert.That(backend.Staged.Count, Is.EqualTo(250));
        }

        [Test]
        public void UnrelatedStagedChangeBlocksEveryBatch()
        {
            var backend = new Backend { UnrelatedStaged = true };
            Assert.That(Service(backend).StageAsync(new RepositoryId("repo"), Selection(125), "Assets/",
                null, CancellationToken.None).GetAwaiter().GetResult().IsFailure, Is.True);
            Assert.That(backend.Batches.Count, Is.EqualTo(0));
        }

        [Test]
        public void SelectingParentDirectoryCannotStageUnselectedChildren()
        {
            var backend = new Backend();
            var result = Service(backend).StageAsync(new RepositoryId("repo"),
                new[] { new RepositoryPath("Assets") }, "Assets/", null,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.IsFailure, Is.True);
            Assert.That(backend.Batches.Count, Is.EqualTo(0));
        }

        [Test]
        public void CancellationAfterOneBatchLeavesInspectablePartialStage()
        {
            var backend = new Backend();
            var service = Service(backend);
            using (var cancellation = new CancellationTokenSource())
            {
                Assert.Throws<OperationCanceledException>(() => service.StageAsync(new RepositoryId("repo"),
                    Selection(125), "Assets/", new CancellingProgress(cancellation), cancellation.Token)
                    .GetAwaiter().GetResult());
            }
            Assert.That(backend.Staged.Count, Is.EqualTo(100));
            Assert.That(service.StageAsync(new RepositoryId("repo"), Selection(125), "Assets/", null,
                CancellationToken.None).GetAwaiter().GetResult().IsSuccess, Is.True);
            Assert.That(backend.Staged.Count, Is.EqualTo(250));
        }
    }
}
