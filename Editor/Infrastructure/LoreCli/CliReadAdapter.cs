using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Repository;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.LoreCli
{
    // Construct only after the configured cache executable has passed Runtime verification.
    // This adapter never asks the system PATH for a Lore binary.
    public sealed class CliReadAdapter : IRepositoryBackend, IStatusBackend
    {
        private readonly LoreCliRunner _runner;
        private readonly CliStatusParser _parser;
        private readonly ConcurrentDictionary<RepositoryId, AbsolutePath> _roots =
            new ConcurrentDictionary<RepositoryId, AbsolutePath>();

        public CliReadAdapter(LoreCliRunner runner, CliStatusParser parser)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        public async Task<Result<RepositorySnapshot>> DetectAsync(AbsolutePath root, CancellationToken token)
        {
            var output = await _runner.RunAsync(root, new[] { "--offline", "status", "--revision-only" }, token);
            if (output.IsFailure) return Result<RepositorySnapshot>.Failure(output.Error);
            if (output.Value.ExitCode != 0)
                return Result<RepositorySnapshot>.Failure(new LoreError(ErrorCode.InvalidRepository,
                    "Lore CLI could not read the repository."));
            var parsed = _parser.ParseRepository(root, output.Value.StandardOutput);
            if (parsed.IsSuccess) _roots[parsed.Value.Id] = parsed.Value.Root;
            return parsed;
        }

        public async Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId repository,
            IReadOnlyList<RepositoryPath> paths, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (!_roots.TryGetValue(repository, out var root))
                return Failure("Repository has not been verified in this session.");
            var args = new List<string> { "--offline", "status" };
            if (paths.Count > 0)
            {
                args.Add("--");
                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path.Value)) return Failure("Invalid targeted status path.");
                    args.Add(path.Value);
                }
            }
            var output = await _runner.RunAsync(root, args, token);
            if (output.IsFailure) return Result<IReadOnlyList<FileStatusEntry>>.Failure(output.Error);
            if (output.Value.ExitCode != 0) return Failure("Lore CLI status failed.");
            var identity = _parser.ParseRepository(root, output.Value.StandardOutput);
            if (identity.IsFailure || !identity.Value.Id.Equals(repository)) return Failure("Lore repository changed.");
            return _parser.ParseFiles(output.Value.StandardOutput);
        }

        private static Result<IReadOnlyList<FileStatusEntry>> Failure(string message) =>
            Result<IReadOnlyList<FileStatusEntry>>.Failure(new LoreError(ErrorCode.InvalidRepository, message));
    }
}
