using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Conflicts;
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
    public sealed class ConflictServiceTests
    {
        private sealed class Journal : IConflictRecoveryJournal
        {
            public bool Applied = true;
            public Result VerifyAppliedMerge(OperationId id, RepositoryId repo) => Applied ? Result.Success() :
                Result.Failure(new LoreError(ErrorCode.ValidationFailed, "No pending merge."));
            public Task<Result> BeginAsync(OperationId id, RepositoryId repo, string operation, CancellationToken token) =>
                throw new Exception("Must not create a second journal.");
            public Task<Result> LoreAppliedAsync(OperationId id, CancellationToken token) =>
                throw new Exception("Must not replace the existing journal.");
            public Task<Result> CompleteAsync(OperationId id, CancellationToken token) =>
                throw new Exception("Must not complete recovery automatically.");
        }
        private sealed class Guard : IWorkingCopyGuard
        {
            public int After;
            public Task<Result> ValidateBeforeWriteAsync(RepositoryId repo, CancellationToken token) =>
                Task.FromResult(Result.Success());
            public Task<Result> ValidateAfterWriteAsync(RepositoryId repo, CancellationToken token)
            { After++; return Task.FromResult(Result.Success()); }
        }
        private sealed class Backend : IConflictBackend, IEditedConflictBackend
        {
            public IReadOnlyList<RepositoryPath> Paths;
            public bool Fail;
            public bool EditedResolved;
            public bool MetaConflicted = true;
            public Task<Result> ChooseVersionAsync(RepositoryId repo, IReadOnlyList<RepositoryPath> paths,
                ConflictChoice choice, CancellationToken token)
            {
                Paths = paths;
                return Task.FromResult(Fail ? Result.Failure(new LoreError(ErrorCode.Unknown, "Missing completion.")) :
                    Result.Success());
            }
            public Task<Result> StageAndResolveAsync(RepositoryId repo, RepositoryPath path, CancellationToken token)
            { EditedResolved = true; return Task.FromResult(Result.Success()); }
        }
        private sealed class Workspace : IConflictTextWorkspace
        {
            public ConflictDraft Draft;
            public string Written;
            public Task<Result<ConflictDraft>> ReadAsync(RepositoryId repo, RepositoryPath path, CancellationToken token) =>
                Task.FromResult(Result<ConflictDraft>.Success(Draft));
            public Task<Result> WriteIfUnchangedAsync(ConflictDraft draft, string result, CancellationToken token)
            { Written = result; return Task.FromResult(Result.Success()); }
        }
        private sealed class External : IExternalMergeExecutor
        {
            public ExternalMergeTool Called;
            public Task<Result<string>> MergeAsync(ExternalMergeTool tool, ConflictDocument document, CancellationToken token)
            { Called = tool; return Task.FromResult(Result<string>.Success(document.Version(1))); }
        }
        private sealed class Status : ISerializedStatusBackend
        {
            private readonly Backend _backend;
            public Status(Backend backend) { _backend = backend; }
            public Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId repo,
                IReadOnlyList<RepositoryPath> paths, CancellationToken token) =>
                ReadUnderLeaseAsync(repo, paths, token);
            public Task<Result<IReadOnlyList<FileStatusEntry>>> ReadUnderLeaseAsync(RepositoryId repo,
                IReadOnlyList<RepositoryPath> paths, CancellationToken token)
            {
                var state = new FileStatus(WorkingState.Modified, StageState.Staged, LockState.Unknown,
                    _backend.Paths == null && !_backend.EditedResolved ? ConflictState.Conflicted :
                        ConflictState.Resolved, RemoteState.Unknown);
                IReadOnlyList<FileStatusEntry> entries = new[]
                {
                    new FileStatusEntry(new RepositoryPath("Assets/a.prefab"), state),
                    new FileStatusEntry(new RepositoryPath("Assets/a.prefab.meta"), _backend.MetaConflicted ? state :
                        new FileStatus(WorkingState.Unchanged, StageState.Unstaged, LockState.Unknown,
                            ConflictState.None, RemoteState.Unknown))
                };
                return Task.FromResult(Result<IReadOnlyList<FileStatusEntry>>.Success(entries));
            }
        }

        [Test]
        public void ChoosesBothConflictedAssetAndMetaWithoutCompletingRecovery()
        {
            var backend = new Backend();
            var guard = new Guard();
            var journal = new Journal();
            var service = new ConflictService(backend, new Status(backend), new RepositoryOperationGate(), guard, journal);
            var id = new OperationId(Guid.NewGuid().ToString("N"));
            var repo = new RepositoryId(new string('a', 32));
            var result = service.ChooseAsync(id, repo, new RepositoryPath("Assets/a.prefab"),
                ConflictChoice.Mine, CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Result.IsSuccess, Is.True);
            Assert.That(backend.Paths.Count, Is.EqualTo(2));
            Assert.That(guard.After, Is.EqualTo(1));
            journal.Applied = false;
            Assert.That(service.ChooseAsync(id, repo, new RepositoryPath("Assets/a.prefab"),
                ConflictChoice.Theirs, CancellationToken.None).Result.Result.IsFailure, Is.True);
        }

        [Test]
        public void AmbiguousCliFailureStillRefreshesUnityAndRetainsRecovery()
        {
            var backend = new Backend { Fail = true };
            var guard = new Guard();
            var service = new ConflictService(backend, new Status(backend), new RepositoryOperationGate(), guard,
                new Journal());
            var outcome = service.ChooseAsync(new OperationId(Guid.NewGuid().ToString("N")),
                new RepositoryId(new string('a', 32)), new RepositoryPath("Assets/a.prefab"),
                ConflictChoice.Theirs, CancellationToken.None).Result;
            Assert.That(outcome.Result.IsFailure, Is.True);
            Assert.That(outcome.LoreApplied, Is.True);
            Assert.That(guard.After, Is.EqualTo(1));
        }

        [Test]
        public void TextResolutionWritesStagesAndKeepsRecoveryPending()
        {
            var backend = new Backend { MetaConflicted = false };
            var repo = new RepositoryId(new string('a', 32));
            var path = new RepositoryPath("Assets/a.prefab");
            var id = new OperationId(Guid.NewGuid().ToString("N"));
            var original = "%YAML 1.1\n--- !u!1 &123\n<<<<<<< ours\n  m_Name: Left\n||||||| original\n  m_Name: Old\n=======\n  m_Name: Old\n>>>>>>> theirs\n";
            var workspace = new Workspace { Draft = new ConflictDraft(repo, path, original, "fingerprint") };
            var external = new External();
            var guard = new Guard();
            var service = new ConflictService(backend, new Status(backend), new RepositoryOperationGate(),
                guard, new Journal(), workspace, external, backend);
            var preview = service.PreviewAsync(id, repo, path, CancellationToken.None).Result;
            Assert.That(preview.IsSuccess, Is.True);
            var outcome = service.ResolveTextAsync(id, repo, path,
                new TextResolutionRequest(TextResolutionMode.Automatic), CancellationToken.None).Result;
            Assert.That(outcome.Result.IsSuccess, Is.True);
            Assert.That(workspace.Written.Contains("m_Name: Left"), Is.True);
            Assert.That(backend.EditedResolved, Is.True);
            Assert.That(guard.After, Is.EqualTo(1));
        }

        [Test]
        public void SelectedExternalToolResultIsAppliedOnlyAfterStatusAndGuard()
        {
            var backend = new Backend { MetaConflicted = false };
            var repo = new RepositoryId(new string('a', 32));
            var path = new RepositoryPath("Assets/a.prefab");
            var original = "<<<<<<< ours\nleft\n||||||| original\nbase\n=======\nright\n>>>>>>> theirs\n";
            var workspace = new Workspace { Draft = new ConflictDraft(repo, path, original, "fingerprint") };
            var external = new External();
            var service = new ConflictService(backend, new Status(backend), new RepositoryOperationGate(),
                new Guard(), new Journal(), workspace, external, backend);
            var tool = new ExternalMergeTool("tool", System.IO.Path.GetFullPath("tool"),
                new[] { "{base}", "{mine}", "{theirs}", "{result}" });
            var outcome = service.ResolveTextAsync(new OperationId(Guid.NewGuid().ToString("N")), repo, path,
                new TextResolutionRequest(TextResolutionMode.External, tool: tool), CancellationToken.None).Result;
            Assert.That(outcome.Result.IsSuccess, Is.True);
            Assert.That(external.Called, Is.SameAs(tool));
            Assert.That(workspace.Written, Is.EqualTo("left\n"));
        }

        [Test]
        public void StaleDraftAndJointMetaConflictNeverWritePartialText()
        {
            var backend = new Backend();
            var repo = new RepositoryId(new string('a', 32));
            var path = new RepositoryPath("Assets/a.prefab");
            var id = new OperationId(Guid.NewGuid().ToString("N"));
            var original = "<<<<<<< ours\nleft\n||||||| original\nbase\n=======\nright\n>>>>>>> theirs\n";
            var workspace = new Workspace { Draft = new ConflictDraft(repo, path, original, "current") };
            var service = new ConflictService(backend, new Status(backend), new RepositoryOperationGate(),
                new Guard(), new Journal(), workspace, new External(), backend);
            var request = new TextResolutionRequest(TextResolutionMode.Manual,
                new ConflictDraft(repo, path, original, "stale"), "manual\n");
            Assert.That(service.ResolveTextAsync(id, repo, path, request, CancellationToken.None).Result.Result.IsFailure,
                Is.True);
            Assert.That(workspace.Written, Is.EqualTo(null));
            backend.MetaConflicted = false;
            Assert.That(service.ResolveTextAsync(id, repo, path, request, CancellationToken.None).Result.Result.IsFailure,
                Is.True);
            Assert.That(workspace.Written, Is.EqualTo(null));
        }
    }
}
