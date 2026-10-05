using System;
using System.Globalization;
using System.Text;

namespace Lore.Unity.Application.Diagnostics
{
    // Export is an allowlist, never a dump of settings, CLI output or logs.
    public sealed class DiagnosticsSummary
    {
        public DiagnosticsSummary(string pluginVersion, string unityVersion, string runtimeVersion,
            string runtimeHealth, bool repositoryPresent, bool statusStale, long? generation,
            DateTime? refreshedUtc, int pendingOperations, int changedFiles)
        {
            PluginVersion = VersionOnly(pluginVersion);
            UnityVersion = VersionOnly(unityVersion);
            RuntimeVersion = VersionOnly(runtimeVersion);
            RuntimeHealth = runtimeHealth == "Ready" || runtimeHealth == "SetupRequired" ||
                runtimeHealth == "UnsupportedPlatform" ? runtimeHealth : "Unknown";
            RepositoryPresent = repositoryPresent;
            StatusStale = statusStale;
            Generation = generation;
            RefreshedUtc = refreshedUtc;
            PendingOperations = pendingOperations;
            ChangedFiles = changedFiles;
        }

        public string PluginVersion { get; }
        public string UnityVersion { get; }
        public string RuntimeVersion { get; }
        public string RuntimeHealth { get; }
        public bool RepositoryPresent { get; }
        public bool StatusStale { get; }
        public long? Generation { get; }
        public DateTime? RefreshedUtc { get; }
        public int PendingOperations { get; }
        public int ChangedFiles { get; }

        public string Export()
        {
            var text = new StringBuilder();
            text.AppendLine("Lore for Unity diagnostics (redacted)");
            text.AppendLine("Plugin: " + PluginVersion);
            text.AppendLine("Unity: " + UnityVersion);
            text.AppendLine("Required Lore: " + RuntimeVersion);
            text.AppendLine("Runtime: " + RuntimeHealth);
            text.AppendLine("Repository detected: " + RepositoryPresent);
            text.AppendLine("Status stale: " + StatusStale);
            text.AppendLine("Status generation: " + (Generation?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"));
            text.AppendLine("Last refresh (UTC): " + (RefreshedUtc?.ToUniversalTime().ToString("u") ?? "unavailable"));
            text.AppendLine("Pending recovery: " + PendingOperations.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("Changed files: " + ChangedFiles.ToString(CultureInfo.InvariantCulture));
            text.AppendLine("Paths, IDs, branch names, revisions, credentials and logs omitted.");
            return text.ToString();
        }

        private static string VersionOnly(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length > 32) return "unavailable";
            foreach (var c in value)
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_') return "unavailable";
            return value;
        }
    }
}
