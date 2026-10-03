using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.LoreCli;

namespace Lore.Unity.Infrastructure.Runtime
{
    // Executable hashes were derived from Epic's v0.10.0 archives after checking
    // their published archive SHA-256. No other version is trusted here.
    public sealed class CliRuntimeProbe : IRuntimeProbe
    {
        private const string WindowsHash = "74bf7d63363f1e8fb185ab54de48f581c9f36bcdd856ee8ed827192de460fb56";
        private const string MacHash = "dff8150035277201f0836a63aece61cf13dc2af7909cb79480098f092169ed87";

        public static AbsolutePath Executable(AbsolutePath installation, string platform)
        {
            if (string.IsNullOrEmpty(installation.Value)) throw new ArgumentException("Installation required.", nameof(installation));
            if (platform != "Windows-x64" && platform != "macOS-arm64")
                throw new ArgumentException("Unsupported platform.", nameof(platform));
            return new AbsolutePath(Path.Combine(installation.Value, platform == "Windows-x64" ? "lore.exe" : "lore"));
        }

        public async Task<Result> VerifyInstalledAsync(AbsolutePath installation, LoreVersion version,
            string platform, CancellationToken cancellationToken)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            var executable = Executable(installation, platform);
            if (version.Value != "0.10.0")
                return Result.Failure(new LoreError(ErrorCode.VersionMismatch, "Lore CLI version has not been verified."));
            var expected = platform == "Windows-x64" ? WindowsHash : MacHash;
            try
            {
                var actual = await Task.Run(() =>
                {
                    using (var stream = File.OpenRead(executable.Value))
                    using (var hash = SHA256.Create())
                        return BitConverter.ToString(hash.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
                }, cancellationToken);
                if (actual != expected)
                    return Result.Failure(new LoreError(ErrorCode.RuntimeCorrupted, "Installed Lore CLI hash mismatch."));
            }
            catch (OperationCanceledException) { throw; }
            catch (IOException) { return Result.Failure(new LoreError(ErrorCode.RuntimeMissing, "Lore CLI cannot be read.")); }
            catch (UnauthorizedAccessException) { return Result.Failure(new LoreError(ErrorCode.RuntimeMissing, "Lore CLI cannot be read.")); }
            cancellationToken.ThrowIfCancellationRequested();
            var result = await new LoreCliRunner(executable).RunAsync(installation,
                new[] { "--version" }, cancellationToken);
            if (result.IsFailure) return Result.Failure(result.Error);
            var line = result.Value.StandardOutput.Trim();
            if (result.Value.ExitCode != 0 ||
                !(line == "lore " + version.Value || line.StartsWith("lore " + version.Value + "+", StringComparison.Ordinal)))
                return Result.Failure(new LoreError(ErrorCode.VersionMismatch, "Installed Lore CLI version differs from the manifest."));
            return Result.Success();
        }
    }
}
