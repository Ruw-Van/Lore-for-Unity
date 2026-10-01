using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class BackendResolverTests
    {
        private sealed class Capabilities : IBackendCapabilityProvider
        {
            public bool Sdk;
            public bool Cli;
            public bool Supports(BackendKind backend, BackendCapability capability) =>
                backend == BackendKind.Sdk ? Sdk : Cli;
        }

        [Test]
        public void PrefersSdkAndKeepsResolvedChoice()
        {
            var capabilities = new Capabilities { Sdk = true, Cli = true };
            var choice = new BackendResolver(capabilities).Resolve(BackendCapability.Status).Value;
            capabilities.Sdk = false;
            Assert.That(choice.Kind, Is.EqualTo(BackendKind.Sdk));
            Assert.That(choice.Capability, Is.EqualTo(BackendCapability.Status));
        }

        [Test]
        public void UsesCliOnlyWhenSdkCannotSupportOperation()
        {
            var result = new BackendResolver(new Capabilities { Cli = true }).Resolve(BackendCapability.Lock);
            Assert.That(result.Value.Kind, Is.EqualTo(BackendKind.Cli));
        }

        [Test]
        public void ReportsUnsupportedWithoutBackend()
        {
            var result = new BackendResolver(new Capabilities()).Resolve(BackendCapability.Merge);
            Assert.That(result.Error.Code, Is.EqualTo(ErrorCode.UnsupportedOperation));
        }
    }
}
