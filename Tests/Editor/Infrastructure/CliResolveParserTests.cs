using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Infrastructure.LoreCli;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class CliResolveParserTests
    {
        [Test]
        public void RequiresAllPathsNativeRevisionAndCompletion()
        {
            var repo = new RepositoryId(new string('a', 32));
            var paths = new[] { new RepositoryPath("Assets/a.prefab") };
            const string file = "{\"tagName\":\"branchMergeResolveFile\",\"data\":{\"path\":\"Assets/a.prefab\"}}\n";
            var revision = "{\"tagName\":\"branchMergeResolveRevision\",\"data\":{\"repository\":\"" +
                repo.Value + "\",\"revision\":\"" + new string('b', 64) + "\"}}\n";
            const string complete = "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n";
            var parser = new CliResolveParser();
            Assert.That(parser.Parse(repo, paths, file + revision + complete).IsSuccess, Is.True);
            Assert.That(parser.Parse(repo, paths, revision + complete).IsFailure, Is.True);
            Assert.That(parser.Parse(repo, paths, file + complete).IsFailure, Is.True);
        }
    }
}
