using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Infrastructure.Runtime;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class RuntimeTests
    {
        private sealed class InstallerSpy : IRuntimeInstaller
        {
            public int Calls;
            public Task<Lore.Unity.Core.Results.Result> InstallAsync(AbsolutePath file, AbsolutePath destination,
                ValidatedRuntimeArtifact artifact, Lore.Unity.Core.Identifiers.LoreVersion version,
                CancellationToken cancellationToken)
            {
                Calls++;
                return Task.FromResult(Lore.Unity.Core.Results.Result.Success());
            }
        }

        private sealed class ProbeSpy : IRuntimeProbe
        {
            public Task<Lore.Unity.Core.Results.Result> VerifyInstalledAsync(AbsolutePath installation,
                Lore.Unity.Core.Identifiers.LoreVersion version, string platform, CancellationToken cancellationToken) =>
                Task.FromResult(Lore.Unity.Core.Results.Result.Success());
        }

        private static RuntimeManifest Manifest(string sha, long size) => new RuntimeManifest
        {
            loreVersion = "test-version",
            artifacts = new System.Collections.Generic.List<RuntimeArtifact> {
                new RuntimeArtifact { platform = "Windows-x64", officialArtifactUrl = "https://example.com/test.zip",
                    sha256 = sha, downloadSize = size, artifactFormat = "zip" }
            }
        };

        [Test]
        public void EmptyManifestIsNotInstallable()
        {
            Assert.That(RuntimeManifestValidator.Validate(new RuntimeManifest()).Error.Code,
                Is.EqualTo(ErrorCode.ValidationFailed));
        }

        [Test]
        public void RejectsInvalidHashUrlAndDuplicatePlatform()
        {
            var manifest = Manifest("bad", 1);
            Assert.That(RuntimeManifestValidator.Validate(manifest).IsFailure, Is.True);
            manifest.artifacts[0].sha256 = new string('a', 64);
            manifest.artifacts[0].officialArtifactUrl = "http://example.com/test.zip";
            Assert.That(RuntimeManifestValidator.Validate(manifest).IsFailure, Is.True);
            manifest.artifacts[0].officialArtifactUrl = "https://example.com/test.zip";
            manifest.artifacts.Add(manifest.artifacts[0]);
            Assert.That(RuntimeManifestValidator.Validate(manifest).IsFailure, Is.True);
        }

        [Test]
        public void ValidatedManifestDoesNotTrackMutableInput()
        {
            var manifest = Manifest(new string('A', 64), 3);
            var valid = RuntimeManifestValidator.Validate(manifest).Value;
            manifest.artifacts.Clear();
            Assert.That(valid.Artifacts.Count, Is.EqualTo(1));
            Assert.That(valid.Artifacts[0].Sha256, Is.EqualTo(new string('a', 64)));
        }

        [Test]
        public void VerifiesLocalBytesWithoutInstallation()
        {
            var bytes = Encoding.UTF8.GetBytes("sample");
            string digest;
            using (var sha = SHA256.Create()) digest = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            var artifact = RuntimeManifestValidator.Validate(Manifest(digest, bytes.Length)).Value.Artifacts[0];
            var manager = new LoreRuntimeManager(RuntimeManifestValidator.Validate(Manifest(digest, bytes.Length)).Value);
            var file = Path.Combine(Path.GetTempPath(), "lore-artifact-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(file, bytes);
                Assert.That(manager.VerifyArtifactAsync(new AbsolutePath(file), artifact, CancellationToken.None)
                    .GetAwaiter().GetResult().IsSuccess, Is.True);
                File.WriteAllText(file, "xxxxxx");
                Assert.That(manager.VerifyArtifactAsync(new AbsolutePath(file), artifact, CancellationToken.None)
                    .GetAwaiter().GetResult().Error.Code, Is.EqualTo(ErrorCode.RuntimeCorrupted));
                File.WriteAllText(file, "broken");
                Assert.That(manager.VerifyArtifactAsync(new AbsolutePath(file), artifact, CancellationToken.None)
                    .GetAwaiter().GetResult().Error.Code, Is.EqualTo(ErrorCode.RuntimeCorrupted));
            }
            finally { if (File.Exists(file)) File.Delete(file); }
        }

        [Test]
        public void CacheLayoutRejectsUnsafeVersion()
        {
            var layout = new RuntimeLayout(new AbsolutePath(Path.GetTempPath()));
            Assert.Throws<ArgumentException>(() => layout.Installation(new Lore.Unity.Core.Identifiers.LoreVersion("../escape"), "Windows-x64"));
            Assert.That(RuntimeManifestValidator.Validate(Manifest(new string('a', 64), 1)).IsSuccess, Is.True);
            var unsafeManifest = Manifest(new string('a', 64), 1);
            unsafeManifest.loreVersion = "../../escape";
            Assert.That(RuntimeManifestValidator.Validate(unsafeManifest).IsFailure, Is.True);
        }

        [Test]
        public void InstallationFailsClosedWithoutVerifiedInstaller()
        {
            var manifest = RuntimeManifestValidator.Validate(Manifest(new string('a', 64), 1)).Value;
            var manager = new LoreRuntimeManager(manifest);
            var file = new AbsolutePath(Path.Combine(Path.GetTempPath(), "missing-lore-artifact"));
            var result = manager.InstallFromFileAsync(file, "Windows-x64", CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(result.Error.Code, Is.EqualTo(ErrorCode.UnsupportedOperation));
        }

        [Test]
        public void CliProbeRejectsMissingInstalledExecutable()
        {
            var dir = new AbsolutePath(Path.Combine(Path.GetTempPath(), "missing-lore-" + Guid.NewGuid().ToString("N")));
            var result = new CliRuntimeProbe().VerifyInstalledAsync(dir,
                new Lore.Unity.Core.Identifiers.LoreVersion("0.10.0"), "Windows-x64", CancellationToken.None)
                .GetAwaiter().GetResult();
            Assert.That(result.Error.Code, Is.EqualTo(ErrorCode.RuntimeMissing));
            Assert.That(CliRuntimeProbe.Executable(dir, "Windows-x64").Value.EndsWith("lore.exe"), Is.True);
        }

        [Test]
        public void CliProbeRejectsTamperedExecutableBeforeRunningIt()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lore-probe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "lore.exe"), "not a verified runtime");
                var result = new CliRuntimeProbe().VerifyInstalledAsync(new AbsolutePath(dir),
                    new Lore.Unity.Core.Identifiers.LoreVersion("0.10.0"), "Windows-x64", CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert.That(result.Error.Code, Is.EqualTo(ErrorCode.RuntimeCorrupted));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Test]
        public void RegistryRoundTripsAndReplacesProjectRecord()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lore-registry-test-" + Guid.NewGuid().ToString("N"));
            var layout = new RuntimeLayout(new AbsolutePath(dir));
            try
            {
                var locks = new FileRuntimeLockManager(layout);
                using (var lease = locks.TryAcquire())
                {
                    Assert.That(lease == null, Is.False);
                    Assert.That(locks.TryAcquire() == null, Is.True);
                    var registry = new FileRuntimeRegistry(layout);
                    var version = new Lore.Unity.Core.Identifiers.LoreVersion("test");
                    registry.Record(new RuntimeProjectRecord(new Lore.Unity.Core.Identifiers.ProjectId("project"), new AbsolutePath(dir), version, DateTime.UtcNow));
                    registry.Record(new RuntimeProjectRecord(new Lore.Unity.Core.Identifiers.ProjectId("project"), new AbsolutePath(dir), version, DateTime.UtcNow));
                    Assert.That(registry.Read().Count, Is.EqualTo(1));
                    Assert.That(registry.Read()[0].RequiredLoreVersion, Is.EqualTo(version));
                }
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void ManagerRecordsProjectOnlyForRequiredVersion()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lore-manager-test-" + Guid.NewGuid().ToString("N"));
            var layout = new RuntimeLayout(new AbsolutePath(dir));
            try
            {
                var manifest = RuntimeManifestValidator.Validate(Manifest(new string('a', 64), 1)).Value;
                var registry = new FileRuntimeRegistry(layout);
                var manager = new LoreRuntimeManager(manifest, layout, new FileRuntimeLockManager(layout),
                    null, null, registry);
                var wrong = new RuntimeProjectRecord(new Lore.Unity.Core.Identifiers.ProjectId("project"),
                    new AbsolutePath(dir), new Lore.Unity.Core.Identifiers.LoreVersion("other"), DateTime.UtcNow);
                Assert.That(manager.RecordProject(wrong).Error.Code, Is.EqualTo(ErrorCode.VersionMismatch));
                Assert.That(Directory.Exists(dir), Is.False);
                var right = new RuntimeProjectRecord(wrong.ProjectId, wrong.ProjectPath, manifest.LoreVersion, DateTime.UtcNow);
                Assert.That(manager.RecordProject(right).IsSuccess, Is.True);
                Assert.That(registry.Read().Count, Is.EqualTo(1));
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }

        [Test]
        public void BadArtifactNeverReachesInstaller()
        {
            var dir = Path.Combine(Path.GetTempPath(), "lore-install-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var artifactFile = Path.Combine(dir, "artifact");
            try
            {
                File.WriteAllText(artifactFile, "wrong");
                var manifest = RuntimeManifestValidator.Validate(Manifest(new string('a', 64), 5)).Value;
                var installer = new InstallerSpy();
                var layout = new RuntimeLayout(new AbsolutePath(dir));
                var manager = new LoreRuntimeManager(manifest, layout, new FileRuntimeLockManager(layout),
                    installer, new ProbeSpy(), null);
                var result = manager.InstallFromFileAsync(new AbsolutePath(artifactFile), "Windows-x64",
                    CancellationToken.None).GetAwaiter().GetResult();
                Assert.That(result.Error.Code, Is.EqualTo(ErrorCode.RuntimeCorrupted));
                Assert.That(installer.Calls, Is.EqualTo(0));
            }
            finally { Directory.Delete(dir, true); }
        }
    }
}
