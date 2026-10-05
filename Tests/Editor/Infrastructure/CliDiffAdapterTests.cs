using Lore.Unity.Core.Paths;
using Lore.Unity.Infrastructure.LoreCli;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class CliDiffAdapterTests
    {
        [Test]
        public void DiffRequiresMatchingPathAndSuccessfulCompletion()
        {
            var path = new RepositoryPath("Assets/a.txt");
            const string diff = "{\"tagName\":\"fileDiff\",\"data\":{\"path\":\"Assets/a.txt\",\"patch\":\"--- a\\n+++ b\\n\"}}\n";
            const string complete = "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n";
            Assert.That(CliDiffAdapter.Parse(path, diff + complete).Value, Is.EqualTo("--- a\n+++ b\n"));
            Assert.That(CliDiffAdapter.Parse(new RepositoryPath("Assets/b.txt"), diff + complete).IsFailure, Is.True);
            Assert.That(CliDiffAdapter.Parse(path, diff).IsFailure, Is.True);
            Assert.That(CliDiffAdapter.Parse(path, complete).IsFailure, Is.True);
        }
    }
}
