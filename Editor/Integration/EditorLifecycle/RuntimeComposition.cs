using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Runtime;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.Backend;
using Lore.Unity.Infrastructure.Recovery;
using Lore.Unity.Infrastructure.Runtime;

namespace Lore.Unity.Integration.EditorLifecycle
{
    public sealed class RuntimeComposition
    {
        private readonly ValidatedRuntimeManifest _manifest;
        private readonly string _platform;

        private RuntimeComposition(RuntimeContext context, LoreRuntimeManager manager,
            ValidatedRuntimeManifest manifest = null, string platform = null, ReadBackendComposition reads = null)
        {
            Context = context;
            Manager = manager;
            _manifest = manifest;
            _platform = platform;
            Reads = reads;
        }
        public RuntimeContext Context { get; }
        public LoreRuntimeManager Manager { get; }
        public ReadBackendComposition Reads { get; }
        public string RequiredVersion => _manifest?.LoreVersion.Value;

        public Result<WriteBackendComposition> CreateWrites(IWorkingCopyGuard guard, FileRecoveryJournal journal)
        {
            if (Context.Availability != RuntimeAvailability.Ready || Reads == null)
                return Result<WriteBackendComposition>.Failure(new Lore.Unity.Core.Errors.LoreError(
                    Lore.Unity.Core.Errors.ErrorCode.RuntimeMissing, "Verified Lore runtime is required."));
            return Reads.CreateWrites(guard, journal);
        }

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
            RuntimeLayout layout;
            try { layout = RuntimeLayout.ForCurrentUser(platform); }
            catch (InvalidOperationException)
            { return new RuntimeComposition(new RuntimeContext(RuntimeAvailability.SetupRequired), null); }
            var probe = new CliRuntimeProbe();
            var manager = new LoreRuntimeManager(validated.Value, layout, new FileRuntimeLockManager(layout),
                new ArchiveRuntimeInstaller(probe), probe, new FileRuntimeRegistry(layout),
                new OfficialRuntimeDownloader(layout));
            if (manager.FindArtifact(platform).IsFailure)
                return new RuntimeComposition(new RuntimeContext(RuntimeAvailability.UnsupportedPlatform), null);
            return new RuntimeComposition(new RuntimeContext(RuntimeAvailability.SetupRequired), manager,
                validated.Value, platform);
        }

        public async Task<RuntimeComposition> ActivateAsync(RuntimeLayout layout, CliRuntimeProbe probe,
            CancellationToken cancellationToken)
        {
            if (_manifest == null || layout == null || probe == null) return this;
            var installed = await probe.VerifyInstalledAsync(layout.Installation(_manifest.LoreVersion, _platform),
                _manifest.LoreVersion, _platform, cancellationToken);
            if (installed.IsFailure) return this;
            var executable = CliRuntimeProbe.Executable(layout.Installation(_manifest.LoreVersion, _platform), _platform);
            var reads = ReadBackendComposition.Create(null, executable, new RepositoryOperationGate());
            return new RuntimeComposition(new RuntimeContext(RuntimeAvailability.Ready), Manager,
                _manifest, _platform, reads);
        }
    }
}
