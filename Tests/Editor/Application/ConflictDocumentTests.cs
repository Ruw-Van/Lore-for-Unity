using Lore.Unity.Application.Conflicts;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class ConflictDocumentTests
    {
        [Test]
        public void FlatYamlFieldsCanBeMergedOnlyWhenEditsDoNotOverlap()
        {
            const string original = "%YAML 1.1\n--- !u!1 &123\n<<<<<<< ours\n  m_Name: Mine\n  m_IsActive: 0\n||||||| original\n  m_Name: Old\n  m_IsActive: 0\n=======\n  m_Name: Old\n  m_IsActive: 1\n>>>>>>> theirs\n";
            var parsed = ConflictDocument.Parse(original);
            Assert.That(parsed.IsSuccess, Is.True);
            Assert.That(parsed.Value.Version(0).Contains("m_Name: Old"), Is.True);
            var resolved = parsed.Value.AutoResolve(true);
            Assert.That(resolved.IsSuccess, Is.True);
            Assert.That(resolved.Value.Contains("  m_Name: Mine\n  m_IsActive: 1"), Is.True);
            Assert.That(parsed.Value.AutoResolve(false).IsFailure, Is.True);
            Assert.That(ConflictDocument.ContainsMarkers(resolved.Value), Is.False);
        }

        [Test]
        public void RejectsNestedIncompleteAndAmbiguousChanges()
        {
            Assert.That(ConflictDocument.Parse("<<<<<<< ours\na\n=======\nb\n>>>>>>> theirs\n").IsFailure, Is.True);
            Assert.That(ConflictDocument.Parse("<<<<<<< ours\na\n||||||| original\nb\n=======\n<<<<<<< ours\n>>>>>>> theirs\n").IsFailure, Is.True);
            const string overlap = "<<<<<<< ours\n  m_Name: One\n||||||| original\n  m_Name: Old\n=======\n  m_Name: Two\n>>>>>>> theirs\n";
            Assert.That(ConflictDocument.Parse(overlap).Value.AutoResolve(true).IsFailure, Is.True);
        }
    }
}
