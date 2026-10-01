using System;
using System.IO;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;

namespace Lore.Unity.Infrastructure.Runtime
{
    public sealed class RuntimeLayout
    {
        public RuntimeLayout(AbsolutePath userCacheRoot)
        {
            if (string.IsNullOrEmpty(userCacheRoot.Value)) throw new ArgumentException("Cache root is required.", nameof(userCacheRoot));
            Root = userCacheRoot;
        }

        public AbsolutePath Root { get; }

        public static RuntimeLayout ForCurrentUser(string platform)
        {
            if (platform == "Windows-x64")
            {
                var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(local)) throw new InvalidOperationException("User cache is unavailable.");
                return new RuntimeLayout(new AbsolutePath(Path.Combine(local, "LoreForUnity", "Runtime")));
            }
            if (platform == "macOS-arm64")
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (string.IsNullOrWhiteSpace(home)) throw new InvalidOperationException("User home is unavailable.");
                return new RuntimeLayout(new AbsolutePath(Path.Combine(home, "Library", "Caches", "LoreForUnity", "Runtime")));
            }
            throw new NotSupportedException("Unsupported platform.");
        }

        public AbsolutePath Installation(LoreVersion version, string platform)
        {
            if (version == null) throw new ArgumentNullException(nameof(version));
            if (!IsSafe(version.Value) || (platform != "Windows-x64" && platform != "macOS-arm64"))
                throw new ArgumentException("Invalid runtime cache key.");
            return new AbsolutePath(Path.Combine(Root.Value, version.Value, platform));
        }

        private static bool IsSafe(string value)
        {
            if (value == "." || value == "..") return false;
            foreach (var c in value)
                if (!char.IsLetterOrDigit(c) && c != '.' && c != '-' && c != '_') return false;
            return true;
        }
    }
}
