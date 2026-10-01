using System;
using System.Runtime.InteropServices;
using Lore.Unity.Application.Runtime;
using Lore.Unity.Infrastructure.Runtime;

namespace Lore.Unity.Integration.EditorLifecycle
{
    public sealed class RuntimeComposition
    {
        private RuntimeComposition(RuntimeContext context, LoreRuntimeManager manager)
        {
            Context = context;
            Manager = manager;
        }
        public RuntimeContext Context { get; }
        public LoreRuntimeManager Manager { get; }

        public static string CurrentPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) &&
                RuntimeInformation.OSArchitecture == Architecture.X64) return "Windows-x64";
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                RuntimeInformation.OSArchitecture == Architecture.Arm64) return "macOS-arm64";
            return null;
        }

        // No I/O, download or backend activation at Editor load. Missing/invalid manifest
        // remains Setup Required; unsupported hosts can still import the package.
        public static RuntimeComposition Create(RuntimeManifest manifest, string platform)
        {
            if (platform == null)
                return new RuntimeComposition(new RuntimeContext(RuntimeAvailability.UnsupportedPlatform), null);
            var validated = RuntimeManifestValidator.Validate(manifest);
            if (validated.IsFailure)
                return new RuntimeComposition(new RuntimeContext(RuntimeAvailability.SetupRequired), null);
            var manager = new LoreRuntimeManager(validated.Value);
            if (manager.FindArtifact(platform).IsFailure)
                return new RuntimeComposition(new RuntimeContext(RuntimeAvailability.UnsupportedPlatform), null);
            return new RuntimeComposition(new RuntimeContext(RuntimeAvailability.SetupRequired), manager);
        }
    }
}
