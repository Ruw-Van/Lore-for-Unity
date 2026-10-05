using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.Runtime
{
    // No PATH lookup or automatic installation. Installer/probe are supplied only after the
    // expected Lore archive format, files and version probe are confirmed.
    public sealed class LoreRuntimeManager
    {
        private readonly ValidatedRuntimeManifest _manifest;
        private readonly RuntimeLayout _layout;
        private readonly IRuntimeLockManager _locks;
        private readonly IRuntimeInstaller _installer;
        private readonly IRuntimeProbe _probe;
        private readonly IRuntimeRegistry _registry;
        private readonly IRuntimeDownloader _downloader;

        public LoreRuntimeManager(ValidatedRuntimeManifest manifest)
            : this(manifest, null, null, null, null, null) { }

        public LoreRuntimeManager(ValidatedRuntimeManifest manifest, RuntimeLayout layout,
            IRuntimeLockManager locks, IRuntimeInstaller installer, IRuntimeProbe probe,
            IRuntimeRegistry registry, IRuntimeDownloader downloader = null)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
            _layout = layout;
            _locks = locks;
            _installer = installer;
            _probe = probe;
            _registry = registry;
            _downloader = downloader;
        }

        // An explicit UI action only. The downloader returns a private archive;
        // the same offline verification/install path is used for both sources.
        public async Task<Result> InstallOfficialAsync(string platform, CancellationToken cancellationToken,
            IProgress<RuntimeInstallProgress> progress = null)
        {
            var choice = FindArtifact(platform);
            if (choice.IsFailure) return Result.Failure(choice.Error);
            if (_downloader == null || _layout == null || _installer == null || _probe == null || _locks == null)
                return Failure(ErrorCode.UnsupportedOperation, "Runtime download is not configured.");
            var file = await _downloader.DownloadAsync(choice.Value, cancellationToken, progress);
            if (file.IsFailure) return Result.Failure(file.Error);
            var root = Path.GetFullPath(_layout.Root.Value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (root.Length == 0) root = Path.DirectorySeparatorChar.ToString();
            var downloaded = Path.GetFullPath(file.Value.Value);
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!string.Equals(Path.GetDirectoryName(downloaded), root, comparison) ||
                !Path.GetFileName(downloaded).StartsWith(".download-", StringComparison.Ordinal))
                return Failure(ErrorCode.ValidationFailed, "Downloader did not return a private runtime archive.");
            try { return await InstallFromFileAsync(new AbsolutePath(downloaded), platform, cancellationToken, progress); }
            finally
            {
                // The downloader owns only this private file, never a user archive.
                try { File.Delete(downloaded); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        public Result<ValidatedRuntimeArtifact> FindArtifact(string platform)
        {
            foreach (var artifact in _manifest.Artifacts)
                if (artifact.Platform == platform) return Result<ValidatedRuntimeArtifact>.Success(artifact);
            return Result<ValidatedRuntimeArtifact>.Failure(new LoreError(ErrorCode.UnsupportedOperation, "Platform is not supported."));
        }

        public async Task<Result> VerifyArtifactAsync(AbsolutePath file, ValidatedRuntimeArtifact artifact,
            CancellationToken cancellationToken)
        {
            if (artifact == null) throw new ArgumentNullException(nameof(artifact));
            if (string.IsNullOrEmpty(file.Value)) throw new ArgumentException("A file is required.", nameof(file));
            try
            {
                using (var stream = new FileStream(file.Value, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true))
                {
                    if (stream.Length != artifact.DownloadSize)
                        return Failure(ErrorCode.RuntimeCorrupted, "Artifact size mismatch.");
                    using (var hash = SHA256.Create())
                    {
                        var buffer = new byte[81920];
                        int count;
                        while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) != 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            hash.TransformBlock(buffer, 0, count, buffer, 0);
                        }
                        hash.TransformFinalBlock(buffer, 0, 0);
                        var digest = BitConverter.ToString(hash.Hash).Replace("-", "").ToLowerInvariant();
                        return digest == artifact.Sha256 ? Result.Success() :
                            Failure(ErrorCode.RuntimeCorrupted, "Artifact SHA-256 mismatch.");
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException) { return Failure(ErrorCode.RuntimeMissing, "Artifact cannot be read."); }
            catch (UnauthorizedAccessException) { return Failure(ErrorCode.RuntimeMissing, "Artifact cannot be read."); }
        }

        public Task<Result> VerifyInstalledAsync(string platform, CancellationToken cancellationToken)
        {
            var choice = FindArtifact(platform);
            if (choice.IsFailure) return Task.FromResult(Result.Failure(choice.Error));
            if (_probe == null || _layout == null)
                return Task.FromResult(Failure(ErrorCode.RuntimeMissing, "Runtime probe is not configured."));
            return _probe.VerifyInstalledAsync(_layout.Installation(_manifest.LoreVersion, platform),
                _manifest.LoreVersion, platform, cancellationToken);
        }

        public Result RecordProject(RuntimeProjectRecord project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            if (!_manifest.LoreVersion.Equals(project.RequiredLoreVersion))
                return Failure(ErrorCode.VersionMismatch, "Project requires a different Lore version.");
            if (_registry == null || _locks == null)
                return Failure(ErrorCode.UnsupportedOperation, "Runtime registry is not configured.");
            using (var lease = _locks.TryAcquire())
            {
                if (lease == null) return Failure(ErrorCode.Locked, "Runtime cache is in use.");
                try { _registry.Record(project); return Result.Success(); }
                catch (IOException) { return Failure(ErrorCode.ValidationFailed, "Runtime registry cannot be written."); }
                catch (UnauthorizedAccessException) { return Failure(ErrorCode.ValidationFailed, "Runtime registry cannot be written."); }
            }
        }

        // Explicit offline install only. A private copy is verified under a cross-process lease;
        // the installer owns archive validation, temporary extraction and atomic publication.
        public async Task<Result> InstallFromFileAsync(AbsolutePath file, string platform,
            CancellationToken cancellationToken, IProgress<RuntimeInstallProgress> progress = null)
        {
            var choice = FindArtifact(platform);
            if (choice.IsFailure) return Result.Failure(choice.Error);
            if (_installer == null || _probe == null || _layout == null || _locks == null)
                return Failure(ErrorCode.UnsupportedOperation, "Runtime installation is not configured.");
            using (var lease = _locks.TryAcquire())
            {
                if (lease == null) return Failure(ErrorCode.Locked, "Runtime cache is in use.");
                cancellationToken.ThrowIfCancellationRequested();
                var destination = _layout.Installation(_manifest.LoreVersion, platform);
                if (Directory.Exists(destination.Value))
                    return Failure(ErrorCode.ValidationFailed, "Runtime already exists; verify or repair it explicitly.");
                var staging = Path.Combine(_layout.Root.Value, ".artifact-" + Guid.NewGuid().ToString("N"));
                try
                {
                    progress?.Report(new RuntimeInstallProgress(RuntimeInstallStage.PreparingArchive));
                    using (var source = new FileStream(file.Value, FileMode.Open, FileAccess.Read, FileShare.Read))
                    using (var target = new FileStream(staging, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                        await source.CopyToAsync(target, 81920, cancellationToken);

                    var privateFile = new AbsolutePath(staging);
                    progress?.Report(new RuntimeInstallProgress(RuntimeInstallStage.VerifyingArchive));
                    var verified = await VerifyArtifactAsync(privateFile, choice.Value, cancellationToken);
                    if (verified.IsFailure) return verified;
                    progress?.Report(new RuntimeInstallProgress(RuntimeInstallStage.Installing));
                    var installed = await _installer.InstallAsync(privateFile, destination, choice.Value,
                        _manifest.LoreVersion, cancellationToken);
                    if (installed.IsFailure) return installed;
                    progress?.Report(new RuntimeInstallProgress(RuntimeInstallStage.VerifyingRuntime));
                    return await _probe.VerifyInstalledAsync(destination, _manifest.LoreVersion, platform, cancellationToken);
                }
                catch (OperationCanceledException) { throw; }
                catch (IOException) { return Failure(ErrorCode.RuntimeMissing, "Artifact cannot be staged."); }
                catch (UnauthorizedAccessException) { return Failure(ErrorCode.RuntimeMissing, "Artifact cannot be staged."); }
                finally
                {
                    try { File.Delete(staging); }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                }
            }
        }

        private static Result Failure(ErrorCode code, string message) => Result.Failure(new LoreError(code, message));
    }
}
