using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.LoreCli;

namespace Lore.Unity.Infrastructure.Repository
{
    // Only creates a local, non-VFS working copy. The caller must then verify
    // the repository with Lore's read backend before exposing it to the Editor.
    public sealed class LoreRepositoryInitializer
    {
        private readonly Func<AbsolutePath, CancellationToken, Task<Result<CliOutput>>> _create;

        public LoreRepositoryInitializer(LoreCliRunner runner)
        {
            if (runner == null) throw new ArgumentNullException(nameof(runner));
            _create = (root, token) => runner.RunAsync(root,
                new[] { "--offline", "repository", "create", "--vfs", "none" }, token);
        }

        // Injectable for offline unit tests. Production always uses the verified CLI.
        public LoreRepositoryInitializer(Func<AbsolutePath, CancellationToken, Task<Result<CliOutput>>> create) =>
            _create = create ?? throw new ArgumentNullException(nameof(create));

        public static bool CanInitialize(AbsolutePath projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot.Value)) return false;
            try
            {
                var directory = Path.GetFullPath(projectRoot.Value);
                if (!Directory.Exists(Path.Combine(directory, "Assets")) ||
                    !Directory.Exists(Path.Combine(directory, "ProjectSettings"))) return false;
                while (directory != null)
                {
                    var marker = Path.Combine(directory, ".lore");
                    if (Directory.Exists(marker) || File.Exists(marker)) return false;
                    directory = Directory.GetParent(directory)?.FullName;
                }
                return true;
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException ||
                                      e is NotSupportedException)
            { return false; }
        }

        public async Task<Result> InitializeAsync(AbsolutePath projectRoot, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (!CanInitialize(projectRoot)) return Failure("Project is already managed or cannot be initialized.");
            var output = await _create(projectRoot, token);
            if (output.IsFailure) return Result.Failure(output.Error);
            if (output.Value.ExitCode != 0) return Failure("Lore could not create a local repository. Inspect its state before retrying.");
            if (!Directory.Exists(Path.Combine(projectRoot.Value, ".lore")))
                return Failure("Lore reported success but the working copy marker is missing.");
            return Result.Success();
        }

        private static Result Failure(string message) => Result.Failure(new LoreError(ErrorCode.InvalidRepository, message));
    }
}
