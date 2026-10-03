using Lore.Unity.Application.Backend;

namespace Lore.Unity.Infrastructure.Backend
{
    // Reflect only actual installed/verified adapter implementations, not CLI help text.
    public sealed class AvailableCapabilities : IBackendCapabilityProvider
    {
        private readonly IBackendSet _sdk;
        private readonly IBackendSet _cli;

        public AvailableCapabilities(IBackendSet sdk, IBackendSet cli)
        {
            _sdk = sdk;
            _cli = cli;
        }

        public bool Supports(BackendKind backend, BackendCapability capability)
        {
            var set = backend == BackendKind.Sdk ? _sdk : backend == BackendKind.Cli ? _cli : null;
            if (set == null) return false;
            var writes = set as IWriteBackendSet;
            switch (capability)
            {
                case BackendCapability.Repository: return set.Repository != null;
                case BackendCapability.Status: return set.Status != null;
                case BackendCapability.Revision: return writes?.Revision != null;
                case BackendCapability.Push: return writes?.Push != null;
                case BackendCapability.Sync: return writes?.Sync != null;
                case BackendCapability.Branch: return writes?.Branch != null;
                case BackendCapability.Lock: return writes?.Lock != null;
                default: return false;
            }
        }
    }
}
