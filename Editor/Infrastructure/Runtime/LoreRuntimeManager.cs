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
    // Read-only foundation. Installation needs a verified official artifact and a safe extractor/probe.
    // No filesystem mutation, PATH lookup, or network access occurs here.
    public sealed class LoreRuntimeManager
    {
        private readonly ValidatedRuntimeManifest _manifest;

        public LoreRuntimeManager(ValidatedRuntimeManifest manifest)
        {
            _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
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

        private static Result Failure(ErrorCode code, string message) => Result.Failure(new LoreError(code, message));
    }
}
