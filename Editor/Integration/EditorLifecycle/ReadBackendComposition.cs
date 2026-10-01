using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Status;
using Lore.Unity.Core.Paths;
using Lore.Unity.Infrastructure.Backend;
using Lore.Unity.Infrastructure.LoreCli;
using Lore.Unity.Infrastructure.LoreSdk;
using Lore.Unity.Infrastructure.Repository;

namespace Lore.Unity.Integration.EditorLifecycle
{
    // Assemble per-project read services only after an executable or SDK is verified.
    // Missing adapters are not advertised. No static service locator is used.
    public sealed class ReadBackendComposition
    {
        private ReadBackendComposition(BackendSession session, StatusStore store)
        {
            Session = session;
            Status = new StatusReader(session, store);
            Store = store;
        }

        public BackendSession Session { get; }
        public StatusReader Status { get; }
        public StatusStore Store { get; }

        public RepositoryDetector CreateDetector()
        {
            var backend = Session.ResolveRepository();
            return backend.IsSuccess ? new RepositoryDetector(backend.Value) : null;
        }

        public static ReadBackendComposition Create(ILoreSdkReadBridge sdkBridge,
            AbsolutePath? verifiedCliExecutable, IRepositoryOperationGate gate)
        {
            IBackendSet sdk = sdkBridge != null && sdkBridge.IsAvailable
                ? new ReadSet(new SdkReadAdapter(sdkBridge)) : null;
            IBackendSet cli = verifiedCliExecutable.HasValue && gate != null
                ? new ReadSet(new CliReadAdapter(new LoreCliRunner(verifiedCliExecutable.Value),
                    new CliStatusParser(), gate)) : null;
            var provider = new AvailableCapabilities(sdk, cli);
            var session = new BackendSession(new BackendResolver(provider), sdk, cli);
            return new ReadBackendComposition(session, new StatusStore());
        }

        private sealed class ReadSet : IBackendSet
        {
            public ReadSet(SdkReadAdapter adapter) { Repository = adapter; Status = adapter; }
            public ReadSet(CliReadAdapter adapter) { Repository = adapter; Status = adapter; }
            public IRepositoryBackend Repository { get; }
            public IStatusBackend Status { get; }
        }
    }
}
