using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.Runtime;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class RuntimeInstallerTests
    {
        private sealed class ProgressRecorder : IProgress<RuntimeInstallProgress>
        {
            public readonly List<RuntimeInstallProgress> Values = new List<RuntimeInstallProgress>();
            public void Report(RuntimeInstallProgress value) => Values.Add(value);
        }

        private sealed class Probe : IRuntimeProbe
        {
            public int Calls;
            public Task<Result> VerifyInstalledAsync(AbsolutePath installation, LoreVersion version,
                string platform, CancellationToken token)
            {
                Calls++;
                return Task.FromResult(File.Exists(Path.Combine(installation.Value,
                    platform == "macOS-arm64" ? "lore" : "lore.exe")) ?
                    Result.Success() : Result.Failure(new Lore.Unity.Core.Errors.LoreError(
                        Lore.Unity.Core.Errors.ErrorCode.RuntimeMissing, "Missing executable.")));
            }
        }

        private sealed class PrivateDownloader : IRuntimeDownloader
        {
            private readonly string _source;
            private readonly RuntimeLayout _layout;
            public string PrivatePath;
            public PrivateDownloader(string source, RuntimeLayout layout) { _source = source; _layout = layout; }
            public Task<Result<AbsolutePath>> DownloadAsync(ValidatedRuntimeArtifact artifact, CancellationToken token,
                IProgress<RuntimeInstallProgress> progress = null)
            {
                Directory.CreateDirectory(_layout.Root.Value);
                PrivatePath = Path.Combine(_layout.Root.Value, ".download-" + Guid.NewGuid().ToString("N"));
                File.Copy(_source, PrivatePath);
                return Task.FromResult(Result<AbsolutePath>.Success(new AbsolutePath(PrivatePath)));
            }
        }

        private static string MakeZip(string root, string entry = "lore.exe")
        {
            var file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".zip");
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.ReadWrite))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                using (var writer = new StreamWriter(zip.CreateEntry(entry).Open()))
                    writer.Write("stub Lore binary");
                if (entry == "lore.exe")
                    using (var writer = new StreamWriter(zip.CreateEntry("LICENSE.txt").Open()))
                        writer.Write("license");
            }
            return file;
        }

        private static ValidatedRuntimeManifest Manifest(string file)
        {
            string sha;
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(file))
                sha = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
            return RuntimeManifestValidator.Validate(new RuntimeManifest
            {
                loreVersion = "0.10.0",
                artifacts = new List<RuntimeArtifact> { new RuntimeArtifact
                {
                    platform = "Windows-x64", artifactFormat = "zip", downloadSize = new FileInfo(file).Length,
                    sha256 = sha,
                    officialArtifactUrl = "https://github.com/EpicGames/lore/releases/download/v0.10.0/lore.zip"
                } }
            }).Value;
        }

        private static string MakeTarGz(string root, string entry)
        {
            var file = Path.Combine(root, Guid.NewGuid().ToString("N") + ".tar.gz");
            using (var target = File.Create(file))
            using (var gzip = new GZipStream(target, CompressionMode.Compress))
            {
                var bytes = Encoding.UTF8.GetBytes("stub Lore binary");
                var header = new byte[512];
                Encoding.ASCII.GetBytes(entry).CopyTo(header, 0);
                Encoding.ASCII.GetBytes("0000755\0").CopyTo(header, 100);
                Encoding.ASCII.GetBytes(Convert.ToString(bytes.Length, 8).PadLeft(11, '0') + "\0").CopyTo(header, 124);
                for (var i = 148; i < 156; i++) header[i] = 32;
                header[156] = (byte)'0';
                long sum = 0;
                foreach (var value in header) sum += value;
                Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(header, 148);
                gzip.Write(header, 0, header.Length);
                gzip.Write(bytes, 0, bytes.Length);
                gzip.Write(new byte[512 - bytes.Length], 0, 512 - bytes.Length);
                gzip.Write(new byte[1024], 0, 1024);
            }
            return file;
        }

        private static ValidatedRuntimeManifest MacManifest(string file)
        {
            string sha;
            using (var hash = SHA256.Create())
            using (var stream = File.OpenRead(file))
                sha = BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "");
            return RuntimeManifestValidator.Validate(new RuntimeManifest
            {
                loreVersion = "0.10.0",
                artifacts = new List<RuntimeArtifact> { new RuntimeArtifact
                {
                    platform = "macOS-arm64", artifactFormat = "tar.gz", downloadSize = new FileInfo(file).Length,
                    sha256 = sha,
                    officialArtifactUrl = "https://github.com/EpicGames/lore/releases/download/v0.10.0/lore.tar.gz"
                } }
            }).Value;
        }

        [Test]
        public void TarGzAcceptsOnlyFlatFilesWithVerifiedHeaders()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-tar-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var good = MakeTarGz(root, "lore");
                var layout = new RuntimeLayout(new AbsolutePath(Path.Combine(root, "cache")));
                var probe = new Probe();
                var manager = new LoreRuntimeManager(MacManifest(good), layout, new FileRuntimeLockManager(layout),
                    new ArchiveRuntimeInstaller(probe), probe, null);
                Assert.That(manager.InstallFromFileAsync(new AbsolutePath(good), "macOS-arm64",
                    CancellationToken.None).Result.IsSuccess, Is.True);
                Assert.That(File.Exists(Path.Combine(layout.Root.Value, "0.10.0", "macOS-arm64", "lore")), Is.True);

                var unsafeArchive = MakeTarGz(root, "../escape");
                var unsafeLayout = new RuntimeLayout(new AbsolutePath(Path.Combine(root, "unsafe-cache")));
                var rejected = new LoreRuntimeManager(MacManifest(unsafeArchive), unsafeLayout,
                    new FileRuntimeLockManager(unsafeLayout), new ArchiveRuntimeInstaller(probe), probe, null);
                Assert.That(rejected.InstallFromFileAsync(new AbsolutePath(unsafeArchive), "macOS-arm64",
                    CancellationToken.None).Result.IsFailure, Is.True);
                Assert.That(File.Exists(Path.Combine(root, "escape")), Is.False);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void LocalArchiveVerifiesBeforeAtomicPublishAndPreservesSource()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-runtime-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var archive = MakeZip(root);
                var layout = new RuntimeLayout(new AbsolutePath(Path.Combine(root, "cache")));
                var probe = new Probe();
                var progress = new ProgressRecorder();
                var manager = new LoreRuntimeManager(Manifest(archive), layout, new FileRuntimeLockManager(layout),
                    new ArchiveRuntimeInstaller(probe), probe, null);
                var result = manager.InstallFromFileAsync(new AbsolutePath(archive), "Windows-x64",
                    CancellationToken.None, progress).Result;
                Assert.That(result.IsSuccess, Is.True);
                Assert.That(progress.Values[0].Stage, Is.EqualTo(RuntimeInstallStage.PreparingArchive));
                Assert.That(progress.Values[1].Stage, Is.EqualTo(RuntimeInstallStage.VerifyingArchive));
                Assert.That(progress.Values[2].Stage, Is.EqualTo(RuntimeInstallStage.Installing));
                Assert.That(progress.Values[3].Stage, Is.EqualTo(RuntimeInstallStage.VerifyingRuntime));
                Assert.That(probe.Calls, Is.EqualTo(2));
                Assert.That(File.Exists(archive), Is.True);
                Assert.That(File.Exists(Path.Combine(layout.Root.Value, "0.10.0", "Windows-x64", "lore.exe")), Is.True);
                Assert.That(manager.InstallFromFileAsync(new AbsolutePath(archive), "Windows-x64",
                    CancellationToken.None).Result.IsFailure, Is.True);
            }
            finally { Directory.Delete(root, true); }
        }

        [Test]
        public void DownloadProgressReportsPinnedBytesAndStopsAtArchiveSize()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-download-progress-" + Guid.NewGuid().ToString("N"));
            var file = Path.Combine(Path.GetTempPath(), "lore-progress-" + Guid.NewGuid().ToString("N"));
            try
            {
                var bytes = new byte[200000];
                File.WriteAllBytes(file, bytes);
                var artifact = Manifest(file).Artifacts[0];
                var recorder = new ProgressRecorder();
                var handler = new Handler { Bytes = bytes };
                var downloaded = new OfficialRuntimeDownloader(new RuntimeLayout(new AbsolutePath(root)),
                    () => handler).DownloadAsync(artifact, CancellationToken.None, recorder).Result;
                Assert.That(downloaded.IsSuccess, Is.True);
                Assert.That(recorder.Values.Count > 2, Is.True);
                Assert.That(recorder.Values[0].BytesReceived, Is.EqualTo(0L));
                long previous = -1;
                foreach (var value in recorder.Values)
                {
                    Assert.That(value.Stage, Is.EqualTo(RuntimeInstallStage.Downloading));
                    Assert.That(value.TotalBytes, Is.EqualTo((long)bytes.Length));
                    Assert.That(value.BytesReceived >= previous, Is.True);
                    Assert.That(value.BytesReceived <= bytes.Length, Is.True);
                    previous = value.BytesReceived;
                }
                Assert.That(previous, Is.EqualTo((long)bytes.Length));
            }
            finally
            {
                File.Delete(file);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Test]
        public void UnsafeEntryNeverPublishesAndPrivateDownloadIsCleaned()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-runtime-unsafe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var archive = MakeZip(root, "../escape.exe");
                var layout = new RuntimeLayout(new AbsolutePath(Path.Combine(root, "cache")));
                var probe = new Probe();
                var downloader = new PrivateDownloader(archive, layout);
                var manager = new LoreRuntimeManager(Manifest(archive), layout, new FileRuntimeLockManager(layout),
                    new ArchiveRuntimeInstaller(probe), probe, null, downloader);
                Assert.That(manager.InstallOfficialAsync("Windows-x64", CancellationToken.None).Result.IsFailure, Is.True);
                Assert.That(File.Exists(downloader.PrivatePath), Is.False);
                Assert.That(Directory.Exists(Path.Combine(layout.Root.Value, "0.10.0", "Windows-x64")), Is.False);
                Assert.That(probe.Calls, Is.EqualTo(0));
            }
            finally { Directory.Delete(root, true); }
        }

        private sealed class Handler : HttpMessageHandler
        {
            public Uri Redirect;
            public byte[] Bytes;
            public int Calls;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                if (Redirect != null && Calls++ == 0)
                {
                    var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                    response.Headers.Location = Redirect;
                    return Task.FromResult(response);
                }
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                { Content = new ByteArrayContent(Bytes) });
            }
        }

        [Test]
        public void DownloaderRejectsUntrustedRedirectAndOversizeWithoutNetwork()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-download-test-" + Guid.NewGuid().ToString("N"));
            var layout = new RuntimeLayout(new AbsolutePath(root));
            var file = Path.Combine(Path.GetTempPath(), "lore-dummy-" + Guid.NewGuid().ToString("N"));
            try
            {
                File.WriteAllBytes(file, new byte[] { 1, 2 });
                var artifact = Manifest(file).Artifacts[0];
                var redirect = new Handler { Redirect = new Uri("https://example.com/other.zip") };
                Assert.That(new OfficialRuntimeDownloader(layout, () => redirect).DownloadAsync(artifact,
                    CancellationToken.None).Result.IsFailure, Is.True);
                var oversized = new Handler { Bytes = new byte[] { 1, 2, 3 } };
                Assert.That(new OfficialRuntimeDownloader(layout, () => oversized).DownloadAsync(artifact,
                    CancellationToken.None).Result.IsFailure, Is.True);
                var trusted = new Handler { Bytes = new byte[] { 1, 2 },
                    Redirect = new Uri("https://release-assets.githubusercontent.com/release-asset") };
                var downloaded = new OfficialRuntimeDownloader(layout, () => trusted).DownloadAsync(artifact,
                    CancellationToken.None).Result;
                Assert.That(downloaded.IsSuccess, Is.True);
                Assert.That(File.ReadAllBytes(downloaded.Value.Value).Length, Is.EqualTo(2));
                File.Delete(downloaded.Value.Value);
            }
            finally
            {
                File.Delete(file);
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }
    }
}
