using Lore.Unity.Application.Diff;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class UnityYamlStructuredDiffTests
    {
        [Test]
        public void GroupsOnlyChangesWithObjectAnchors()
        {
            const string diff = "--- Assets/a.prefab\n+++ Assets/a.prefab\n@@ -1,3 +1,3 @@\n %YAML 1.1\n --- !u!1 &100\n-  m_Name: Old\n+  m_Name: New\n";
            var result = UnityYamlStructuredDiff.Format(diff);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value.Contains("m_Name: New"), Is.True);
            Assert.That(UnityYamlStructuredDiff.Format("@@ -20 +20 @@\n-old\n+new\n").IsFailure, Is.True);
        }
    }
}
