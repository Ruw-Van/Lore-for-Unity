using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Repository;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.Backend;
using Lore.Unity.Infrastructure.LoreCli;
using Lore.Unity.Infrastructure.LoreSdk;
using Lore.Unity.Infrastructure.Repository;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class Phase2ReadTests
    {
        private sealed class Bridge : ILoreSdkReadBridge
        {
            public bool IsAvailable => true;
            public SdkRepositoryData Repository;
            public IReadOnlyList<SdkFileData> Files;
            public Task<Result<SdkRepositoryData>> ReadRepositoryAsync(AbsolutePath root, CancellationToken token) =>
                Task.FromResult(Result<SdkRepositoryData>.Success(Repository));
            public Task<Result<IReadOnlyList<SdkFileData>>> ReadStatusAsync(RepositoryId id,
                IReadOnlyList<RepositoryPath> paths, CancellationToken token) =>
                Task.FromResult(Result<IReadOnlyList<SdkFileData>>.Success(Files));
        }

        private sealed class Set : IBackendSet
        {
            public IRepositoryBackend Repository { get; set; }
            public IStatusBackend Status { get; set; }
        }

        [Test]
        public void CapabilityIsAbsentWhenAdapterIsNotInstalled()
        {
            var provider = new AvailableCapabilities(null, new Set());
            Assert.That(provider.Supports(BackendKind.Sdk, BackendCapability.Status), Is.False);
            Assert.That(provider.Supports(BackendKind.Cli, BackendCapability.Status), Is.False);
            Assert.That(new BackendResolver(provider).Resolve(BackendCapability.Status).Error.Code,
                Is.EqualTo(ErrorCode.UnsupportedOperation));
        }

        [Test]
        public void SdkAdapterRejectsInvalidPathsInsteadOfPublishingPartialStatus()
        {
            var bridge = new Bridge { Files = new[] { new SdkFileData { Path = "../escape" } } };
            var result = new SdkReadAdapter(bridge).ReadAsync(new RepositoryId("repo"),
                new RepositoryPath[0], CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Error.Code, Is.EqualTo(ErrorCode.ValidationFailed));
        }

        [Test]
        public void SdkAdapterMapsValidatedFileStatus()
        {
            var bridge = new Bridge { Files = new[] { new SdkFileData {
                Path = "Assets/a.prefab.meta", Working = Lore.Unity.Core.Status.WorkingState.Modified,
                Stage = Lore.Unity.Core.Status.StageState.Unstaged } } };
            var result = new SdkReadAdapter(bridge).ReadAsync(new RepositoryId("repo"),
                new RepositoryPath[0], CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Value.Count, Is.EqualTo(1));
            Assert.That(result.Value[0].Path.Value, Is.EqualTo("Assets/a.prefab.meta"));
            Assert.That(result.Value[0].Status.Working,
                Is.EqualTo(Lore.Unity.Core.Status.WorkingState.Modified));
        }

        [Test]
        public void BackendSessionDoesNotSwitchWhenChosenAdapterIsUnavailable()
        {
            var provider = new FixedCapabilities();
            var cli = new Set { Status = new SdkReadAdapter(new Bridge()) };
            var session = new BackendSession(new BackendResolver(provider), null, cli);
            Assert.That(session.ResolveStatus().Error.Code, Is.EqualTo(ErrorCode.UnsupportedOperation));
        }

        private sealed class FixedCapabilities : IBackendCapabilityProvider
        {
            public bool Supports(BackendKind kind, BackendCapability capability) => kind == BackendKind.Sdk;
        }

        [Test]
        public void RepositoryMarkerIsOnlyHintAndIdentityComesFromBackend()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-detection-" + Guid.NewGuid().ToString("N"));
            var project = Path.Combine(root, "UnityProject");
            Directory.CreateDirectory(Path.Combine(root, ".lore"));
            Directory.CreateDirectory(project);
            try
            {
                var bridge = new Bridge { Repository = new SdkRepositoryData {
                    Id = "lore-identity", Root = root, Branch = "main", Revision = "signature" } };
                var detector = new RepositoryDetector(new SdkReadAdapter(bridge));
                var found = detector.DetectAsync(new AbsolutePath(project), CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert.That(found.Value.Id.Value, Is.EqualTo("lore-identity"));
                Assert.That(found.Value.Root, Is.EqualTo(new AbsolutePath(root)));
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void CliNeverFallsBackToSystemPath()
        {
            var missing = new AbsolutePath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "lore.exe"));
            var runner = new LoreCliRunner(missing);
            var result = runner.RunAsync(new AbsolutePath(Path.GetTempPath()), new[] { "status" },
                CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Error.Code, Is.EqualTo(ErrorCode.RuntimeMissing));
        }

        [Test]
        public void CliOutputLimitRemainsBoundedForStatusScans()
        {
            var missing = new AbsolutePath(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "lore.exe"));
            var runner = new LoreCliRunner(missing);
            Assert.Throws<ArgumentOutOfRangeException>(() => runner.RunAsync(new AbsolutePath(Path.GetTempPath()),
                new[] { "--json", "--offline", "status", "--scan" }, CancellationToken.None,
                LoreCliRunner.MaxStatusChars + 1));
            var allowed = runner.RunAsync(new AbsolutePath(Path.GetTempPath()),
                new[] { "--json", "--offline", "status", "--scan" }, CancellationToken.None,
                LoreCliRunner.MaxStatusChars).GetAwaiter().GetResult();
            Assert.That(allowed.Error.Code, Is.EqualTo(ErrorCode.RuntimeMissing));
        }

        [Test]
        public void CliParserAcceptsFullStatusLargerThanOneMiB()
        {
            var output = new System.Text.StringBuilder(RevisionJson()).Append('\n');
            for (var i = 0; i < 7500; i++)
                output.Append("{\"tagName\":\"repositoryStatusFile\",\"data\":{\"path\":\"Assets/File")
                    .Append(i).Append(".asset\",\"action\":\"add\",\"flagDirty\":true,")
                    .Append("\"flagStaged\":false,\"flagConflict\":false,\"flagConflictUnresolved\":false}}\n");
            output.Append(CompleteJson());
            Assert.That(output.Length > 1024 * 1024, Is.True);
            var parsed = new CliStatusParser().Parse(new AbsolutePath(Path.GetTempPath()), output.ToString());
            Assert.That(parsed.IsSuccess, Is.True);
            Assert.That(parsed.Value.Files.Count, Is.EqualTo(7500));
        }

        [Test]
        public void CliParserReadsStructuredStatusWithoutInventingLockState()
        {
            var text = RevisionJson() + "\n" +
                "{\"tagName\":\"repositoryStatusFile\",\"data\":{\"path\":\"Assets/Scene.unity\",\"action\":\"keep\",\"flagDirty\":true,\"flagStaged\":false,\"flagConflict\":false,\"flagConflictUnresolved\":false}}\n" +
                "{\"tagName\":\"repositoryStatusFile\",\"data\":{\"path\":\"Assets/new.prefab\",\"action\":\"add\",\"flagDirty\":true,\"flagStaged\":false,\"flagConflict\":false,\"flagConflictUnresolved\":false}}\n" +
                "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n";
            var parser = new CliStatusParser();
            var root = new AbsolutePath(Path.GetTempPath());
            var parsed = parser.Parse(root, text).Value;
            Assert.That(parsed.Repository.Id.Value, Is.EqualTo(new string('a', 32)));
            var files = parsed.Files;
            Assert.That(files.Count, Is.EqualTo(2));
            Assert.That(files[0].Status.Lock, Is.EqualTo(Lore.Unity.Core.Status.LockState.Unknown));
            Assert.That(files[1].Status.Working, Is.EqualTo(Lore.Unity.Core.Status.WorkingState.Untracked));
        }

        [Test]
        public void CliParserRejectsAmbiguousOrIncompleteStreams()
        {
            var parser = new CliStatusParser();
            var root = new AbsolutePath(Path.GetTempPath());
            Assert.That(parser.Parse(root, RevisionJson()).IsFailure, Is.True);
            Assert.That(parser.Parse(root, RevisionJson() + "\n{\"tagName\":\"newStatusEvent\",\"data\":{}}\n" + CompleteJson()).IsFailure, Is.True);
            Assert.That(parser.Parse(root, RevisionJson() + "\n{\"tagName\":\"repositoryStatusFile\",\"data\":{\"path\":\"../outside\",\"action\":\"add\",\"flagDirty\":true,\"flagStaged\":false,\"flagConflict\":false,\"flagConflictUnresolved\":false}}\n" + CompleteJson()).IsFailure, Is.True);
            Assert.That(parser.Parse(root, RevisionJson() + "\n{\"tagName\":\"complete\",\"data\":{\"status\":4}}\n").IsFailure, Is.True);
            Assert.That(parser.Parse(root, RevisionJson() + "\n{\"tagName\":\"repositoryStatusFile\",\"data\":{\"path\":\"Assets/a\",\"action\":\"copy\",\"flagDirty\":true,\"flagStaged\":true,\"flagConflict\":false,\"flagConflictUnresolved\":false}}\n" + CompleteJson()).IsFailure, Is.True);
        }

        [Test]
        public void CliParserRetainsMoveSourcePath()
        {
            var text = RevisionJson() + "\n{\"tagName\":\"repositoryStatusFile\",\"data\":{\"path\":\"Assets/new.prefab\",\"fromPath\":\"Assets/old.prefab\",\"action\":\"move\",\"flagDirty\":false,\"flagStaged\":true,\"flagConflict\":false,\"flagConflictUnresolved\":false}}\n" + CompleteJson();
            var parsed = new CliStatusParser().Parse(new AbsolutePath(Path.GetTempPath()), text);
            Assert.That(parsed.Value.Files[0].Status.Working,
                Is.EqualTo(Lore.Unity.Core.Status.WorkingState.Moved));
            Assert.That(parsed.Value.Files[0].SourcePath.Value.Value, Is.EqualTo("Assets/old.prefab"));
        }

        [Test]
        public void ScanGateSerializesWithinRepository()
        {
            var gate = new RepositoryOperationGate();
            var id = new RepositoryId("repo");
            Task<IDisposable> waiter;
            using (gate.AcquireAsync(id, CancellationToken.None).GetAwaiter().GetResult())
            {
                waiter = gate.AcquireAsync(new RepositoryId("repo"), CancellationToken.None);
                Assert.That(waiter.IsCompleted, Is.False);
            }
            using (waiter.GetAwaiter().GetResult()) { }
            using (gate.AcquireAsync(id, CancellationToken.None).GetAwaiter().GetResult()) { }
        }

        private static string RevisionJson() => "{\"tagName\":\"repositoryStatusRevision\",\"data\":{\"repository\":\"" +
            new string('a', 32) + "\",\"branchName\":\"main\",\"revision\":\"" + new string('b', 64) + "\"}}";

        private static string CompleteJson() => "{\"tagName\":\"complete\",\"data\":{\"status\":0}}";
    }
}
