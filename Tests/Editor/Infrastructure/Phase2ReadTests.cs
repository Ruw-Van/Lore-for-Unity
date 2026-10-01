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
        public void CliParserReadsDocumentedSimpleStatusWithoutInventingLockState()
        {
            var text = "Repository " + new string('a', 32) + "\nOn branch main revision 1 -> " +
                new string('b', 64) + "\nChanges not staged for commit:\nM Assets/Scene.unity\n" +
                "Untracked files:\nA Assets/new.prefab\n";
            var parser = new CliStatusParser();
            var root = new AbsolutePath(Path.GetTempPath());
            Assert.That(parser.ParseRepository(root, text).Value.Id.Value, Is.EqualTo(new string('a', 32)));
            var files = parser.ParseFiles(text).Value;
            Assert.That(files.Count, Is.EqualTo(2));
            Assert.That(files[0].Status.Lock, Is.EqualTo(Lore.Unity.Core.Status.LockState.Unknown));
            Assert.That(files[1].Status.Working, Is.EqualTo(Lore.Unity.Core.Status.WorkingState.Untracked));
        }

        [Test]
        public void CliParserRejectsAmbiguousStagedAndUnrecognizedOutput()
        {
            var header = "Repository " + new string('a', 32) + "\nOn branch main revision 1 -> " +
                new string('b', 64) + "\n";
            var parser = new CliStatusParser();
            Assert.That(parser.ParseFiles(header + "Changes staged for commit:\nA Assets/x.prefab\n").IsFailure, Is.True);
            Assert.That(parser.ParseFiles(header + "Changes not staged for commit:\nM ../outside\n").IsFailure, Is.True);
            Assert.That(parser.ParseFiles(header + "New output format\n").IsFailure, Is.True);
        }
    }
}
