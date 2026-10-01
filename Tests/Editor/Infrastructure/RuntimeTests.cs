using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Infrastructure.Runtime;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class RuntimeTests
    {
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
    }
}
