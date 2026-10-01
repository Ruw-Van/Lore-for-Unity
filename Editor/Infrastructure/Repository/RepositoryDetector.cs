using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Repository;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.Repository
{
    public sealed class RepositoryDetector
    {
        private readonly IRepositoryBackend _backend;

        public RepositoryDetector(IRepositoryBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public async Task<Result<RepositorySnapshot>> DetectAsync(AbsolutePath projectRoot,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(projectRoot.Value)) throw new ArgumentException("Project root required.", nameof(projectRoot));
            return await Task.Run(() => DetectCoreAsync(projectRoot, cancellationToken), cancellationToken);
        }

        private async Task<Result<RepositorySnapshot>> DetectCoreAsync(AbsolutePath projectRoot,
            CancellationToken cancellationToken)
        {
            var directory = projectRoot.Value;
            while (directory != null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                // The marker is only a hint. Lore itself must provide the identity and state.
                if (Directory.Exists(Path.Combine(directory, ".lore")))
                {
                    var result = await _backend.DetectAsync(new AbsolutePath(directory), cancellationToken);
                    if (result.IsFailure) return result;
                    if (result.Value == null || !result.Value.Root.Equals(new AbsolutePath(directory)))
                        return Result<RepositorySnapshot>.Failure(new LoreError(ErrorCode.InvalidRepository,
                            "Lore returned a different repository root."));
                    return result;
                }
                var parent = Directory.GetParent(directory);
                directory = parent?.FullName;
            }
            return Result<RepositorySnapshot>.Failure(new LoreError(ErrorCode.InvalidRepository,
                "No Lore working copy was found."));
        }
    }
}
