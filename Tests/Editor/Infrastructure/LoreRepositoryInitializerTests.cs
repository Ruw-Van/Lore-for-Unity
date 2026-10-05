using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.LoreCli;
using Lore.Unity.Infrastructure.Repository;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class LoreRepositoryInitializerTests
    {
        private static string Project(string parent)
        {
            var root = Path.Combine(parent, "UnityProject");
            Directory.CreateDirectory(Path.Combine(root, "Assets"));
            Directory.CreateDirectory(Path.Combine(root, "ProjectSettings"));
            return root;
        }

        [Test]
        public void CreatesOnlyUnmanagedProjectAndRequiresMarker()
        {
            var parent = Path.Combine(Path.GetTempPath(), "lore-init-test-" + Guid.NewGuid().ToString("N"));
            var root = Project(parent);
            try
            {
                var calls = 0;
                var initializer = new LoreRepositoryInitializer((directory, token) =>
                {
                    calls++;
                    Directory.CreateDirectory(Path.Combine(directory.Value, ".lore"));
                    return Task.FromResult(Result<CliOutput>.Success(new CliOutput(0, "", "")));
                });
                Assert.That(LoreRepositoryInitializer.CanInitialize(new AbsolutePath(root)), Is.True);
                Assert.That(initializer.InitializeAsync(new AbsolutePath(root), CancellationToken.None).Result.IsSuccess,
                    Is.True);
                Assert.That(LoreRepositoryInitializer.CanInitialize(new AbsolutePath(root)), Is.False);
                Assert.That(initializer.InitializeAsync(new AbsolutePath(root), CancellationToken.None).Result.IsFailure,
                    Is.True);
                Assert.That(calls, Is.EqualTo(1));
            }
            finally { Directory.Delete(parent, true); }
        }

        [Test]
        public void AncestorMarkerAndInvalidMarkerBlockInitialization()
        {
            var parent = Path.Combine(Path.GetTempPath(), "lore-init-guard-" + Guid.NewGuid().ToString("N"));
            var root = Project(parent);
            try
            {
                var calls = 0;
                var initializer = new LoreRepositoryInitializer((directory, token) =>
                {
                    calls++;
                    return Task.FromResult(Result<CliOutput>.Success(new CliOutput(0, "", "")));
                });
                Directory.CreateDirectory(Path.Combine(parent, ".lore"));
                Assert.That(LoreRepositoryInitializer.CanInitialize(new AbsolutePath(root)), Is.False);
                Directory.Delete(Path.Combine(parent, ".lore"));
                File.WriteAllText(Path.Combine(root, ".lore"), "invalid marker");
                Assert.That(LoreRepositoryInitializer.CanInitialize(new AbsolutePath(root)), Is.False);
                Assert.That(initializer.InitializeAsync(new AbsolutePath(root), CancellationToken.None).Result.IsFailure,
                    Is.True);
                Assert.That(calls, Is.EqualTo(0));
            }
            finally { Directory.Delete(parent, true); }
        }

        [Test]
        public void FailedOrUnverifiedCreationDoesNotClaimSuccess()
        {
            var parent = Path.Combine(Path.GetTempPath(), "lore-init-failure-" + Guid.NewGuid().ToString("N"));
            var root = Project(parent);
            try
            {
                var failed = new LoreRepositoryInitializer((directory, token) =>
                    Task.FromResult(Result<CliOutput>.Success(new CliOutput(1, "", "failure"))));
                Assert.That(failed.InitializeAsync(new AbsolutePath(root), CancellationToken.None).Result.IsFailure,
                    Is.True);
                var unverified = new LoreRepositoryInitializer((directory, token) =>
                    Task.FromResult(Result<CliOutput>.Success(new CliOutput(0, "", ""))));
                Assert.That(unverified.InitializeAsync(new AbsolutePath(root), CancellationToken.None).Result.IsFailure,
                    Is.True);
            }
            finally { Directory.Delete(parent, true); }
        }
    }
}
