using System;
using System.IO;
using System.Threading;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Infrastructure.LoreCli;
using Lore.Unity.Infrastructure.Recovery;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class FileConflictTextWorkspaceTests
    {
        private const string Conflict = "<<<<<<< ours\nleft\n||||||| original\nbase\n=======\nright\n>>>>>>> theirs\n";

        [Test]
        public void RejectsStaleAndUnresolvedResultsWithoutReplacingFile()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-text-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            try
            {
                var file = Path.Combine(root, "Assets", "a.txt");
                File.WriteAllText(file, Conflict);
                var repo = new RepositoryId("repo");
                var relative = new RepositoryPath("Assets/a.txt");
                var roots = new RepositoryLocations();
                roots.Record(repo, new AbsolutePath(root));
                var workspace = new FileConflictTextWorkspace(roots);
                var draft = workspace.ReadAsync(repo, relative, CancellationToken.None).Result.Value;
                Assert.That(workspace.WriteIfUnchangedAsync(draft, Conflict, CancellationToken.None).Result.IsFailure,
                    Is.True);
                File.WriteAllText(file, "other");
                Assert.That(workspace.WriteIfUnchangedAsync(draft, "resolved\n", CancellationToken.None).Result.IsFailure,
                    Is.True);
                Assert.That(File.ReadAllText(file), Is.EqualTo("other"));
                File.WriteAllText(file, Conflict);
                draft = workspace.ReadAsync(repo, relative, CancellationToken.None).Result.Value;
                Assert.That(workspace.WriteIfUnchangedAsync(draft, "resolved\n", CancellationToken.None).Result.IsSuccess,
                    Is.True);
                Assert.That(File.ReadAllText(file), Is.EqualTo("resolved\n"));
            }
            finally { Directory.Delete(root, true); }
        }
    }
}
