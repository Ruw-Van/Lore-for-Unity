using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.Runtime
{
    public sealed class ValidatedRuntimeArtifact
    {
        internal ValidatedRuntimeArtifact(string platform, Uri url, string sha256, long size, string format)
        {
            Platform = platform;
            OfficialArtifactUrl = url;
            Sha256 = sha256;
            DownloadSize = size;
            ArtifactFormat = format;
        }
        public string Platform { get; }
        public Uri OfficialArtifactUrl { get; }
        public string Sha256 { get; }
        public long DownloadSize { get; }
        public string ArtifactFormat { get; }
    }

    public sealed class ValidatedRuntimeManifest
    {
        internal ValidatedRuntimeManifest(LoreVersion version, IList<ValidatedRuntimeArtifact> artifacts)
        {
            LoreVersion = version;
            Artifacts = new ReadOnlyCollection<ValidatedRuntimeArtifact>(artifacts);
        }
        public LoreVersion LoreVersion { get; }
        public IReadOnlyList<ValidatedRuntimeArtifact> Artifacts { get; }
    }

    public static class RuntimeManifestValidator
    {
        public static Result<ValidatedRuntimeManifest> Validate(RuntimeManifest manifest)
        {
            if (manifest == null || string.IsNullOrWhiteSpace(manifest.loreVersion) ||
                manifest.artifacts == null || manifest.artifacts.Count == 0)
                return Invalid("A version and at least one artifact are required.");

            var artifacts = new List<ValidatedRuntimeArtifact>();
            var platforms = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in manifest.artifacts)
            {
                if (entry == null || (entry.platform != "Windows-x64" && entry.platform != "macOS-arm64") ||
                    !platforms.Add(entry.platform) || !IsSha256(entry.sha256) || entry.downloadSize <= 0 ||
                    string.IsNullOrWhiteSpace(entry.artifactFormat) ||
                    !Uri.TryCreate(entry.officialArtifactUrl, UriKind.Absolute, out var url) ||
                    url.Scheme != Uri.UriSchemeHttps || !string.IsNullOrEmpty(url.UserInfo) ||
                    !string.IsNullOrEmpty(url.Fragment))
                    return Invalid("Invalid artifact metadata, duplicate platform, or non-HTTPS URL.");

                artifacts.Add(new ValidatedRuntimeArtifact(entry.platform, url,
                    entry.sha256.ToLowerInvariant(), entry.downloadSize, entry.artifactFormat));
            }
            return Result<ValidatedRuntimeManifest>.Success(
                new ValidatedRuntimeManifest(new LoreVersion(manifest.loreVersion), artifacts));
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64) return false;
            foreach (var c in value)
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                    return false;
            return true;
        }

        private static Result<ValidatedRuntimeManifest> Invalid(string message) =>
            Result<ValidatedRuntimeManifest>.Failure(new LoreError(ErrorCode.ValidationFailed, message));
    }
}
