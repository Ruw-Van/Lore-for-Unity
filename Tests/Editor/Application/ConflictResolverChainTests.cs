using Lore.Unity.Application.Conflicts;
using Lore.Unity.Core.Paths;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class ConflictResolverChainTests
    {
        [Test]
        public void CandidatesFollowConfiguredOrderAndNeverAssumeExternalTool()
        {
            var ordinary = new ConflictResolverChain().Candidates(new RepositoryPath("Assets/a.prefab"));
            Assert.That(ordinary[0], Is.EqualTo(ResolverKind.UnityStructured));
            Assert.That(ordinary[1], Is.EqualTo(ResolverKind.Text));
            Assert.That(ordinary[2], Is.EqualTo(ResolverKind.BinaryChooseVersion));
            var configured = new ConflictResolverChain(true).Candidates(new RepositoryPath("Assets/a.prefab"));
            Assert.That(configured[1], Is.EqualTo(ResolverKind.ExternalTool));
            var binary = new ConflictResolverChain().Candidates(new RepositoryPath("Assets/a.png"));
            Assert.That(binary.Count, Is.EqualTo(1));
            Assert.That(binary[0], Is.EqualTo(ResolverKind.BinaryChooseVersion));
        }
    }
}
