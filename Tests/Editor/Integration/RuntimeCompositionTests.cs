using Lore.Unity.Application.Runtime;
using Lore.Unity.Integration.EditorLifecycle;
using Lore.Unity.Infrastructure.Runtime;
using NUnit.Framework;

namespace Lore.Unity.Tests.Integration
{
    public sealed class RuntimeCompositionTests
    {
        [Test]
        public void MissingManifestDoesNotActivateRuntime()
        {
            var composition = RuntimeComposition.Create(null, "Windows-x64");
            Assert.That(composition.Context.Availability, Is.EqualTo(RuntimeAvailability.SetupRequired));
            Assert.That(composition.Manager == null, Is.True);
        }

        [Test]
        public void UnsupportedHostDoesNotActivateRuntime()
        {
            var composition = RuntimeComposition.Create(null, null);
            Assert.That(composition.Context.Availability, Is.EqualTo(RuntimeAvailability.UnsupportedPlatform));
        }

        [Test]
        public void ValidManifestConstructsManagerButDoesNotAssumeInstalled()
        {
            var manifest = new RuntimeManifest { loreVersion = "test", artifacts =
                new System.Collections.Generic.List<RuntimeArtifact> {
                    new RuntimeArtifact { platform = "Windows-x64", officialArtifactUrl = "https://example.com/archive",
                        sha256 = new string('a', 64), downloadSize = 10, artifactFormat = "zip" }
                } };
            var composition = RuntimeComposition.Create(manifest, "Windows-x64");
            Assert.That(composition.Manager == null, Is.False);
            Assert.That(composition.Context.Availability, Is.EqualTo(RuntimeAvailability.SetupRequired));
        }
    }
}
