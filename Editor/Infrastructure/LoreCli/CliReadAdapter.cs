using System;
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
    public sealed class CliReadAdapter : IRepositoryBackend, ISerializedStatusBackend
    {
        private readonly LoreCliRunner _runner;
        private readonly CliStatusParser _parser;
        private readonly IRepositoryOperationGate _gate;
        private readonly RepositoryLocations _roots;

        public CliReadAdapter(LoreCliRunner runner, CliStatusParser parser, IRepositoryOperationGate gate)
            : this(runner, parser, gate, new RepositoryLocations()) { }

        public CliReadAdapter(LoreCliRunner runner, CliStatusParser parser, IRepositoryOperationGate gate,
            RepositoryLocations roots)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        }

        public async Task<Result<RepositorySnapshot>> DetectAsync(AbsolutePath root, CancellationToken token)
        {
            var output = await _runner.RunAsync(root, new[] { "--json", "--offline", "status", "--revision-only" }, token);
            if (output.IsFailure) return Result<RepositorySnapshot>.Failure(output.Error);
            if (output.Value.ExitCode != 0)
                return Result<RepositorySnapshot>.Failure(new LoreError(ErrorCode.InvalidRepository,
                    "Lore CLI could not read the repository."));
            var parsed = _parser.Parse(root, output.Value.StandardOutput);
            if (parsed.IsFailure) return Result<RepositorySnapshot>.Failure(parsed.Error);
            _roots.Record(parsed.Value.Repository.Id, parsed.Value.Repository.Root);
            return Result<RepositorySnapshot>.Success(parsed.Value.Repository);
        }

        public async Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId repository,
            IReadOnlyList<RepositoryPath> paths, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (!_roots.TryGet(repository, out _))
                return Failure("Repository has not been verified in this session.");
            using (await _gate.AcquireAsync(repository, token))
                return await ReadUnderLeaseAsync(repository, paths, token);
        }

        public async Task<Result<IReadOnlyList<FileStatusEntry>>> ReadUnderLeaseAsync(RepositoryId repository,
            IReadOnlyList<RepositoryPath> paths, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (!_roots.TryGet(repository, out var root)) return Failure("Repository has not been verified in this session.");
            var args = new List<string> { "--json", "--offline", "status", "--scan" };
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
            var parsed = _parser.Parse(root, output.Value.StandardOutput);
            if (parsed.IsFailure) return Result<IReadOnlyList<FileStatusEntry>>.Failure(parsed.Error);
            if (!parsed.Value.Repository.Id.Equals(repository)) return Failure("Lore repository changed.");
            return Result<IReadOnlyList<FileStatusEntry>>.Success(parsed.Value.Files);
        }

        private static Result<IReadOnlyList<FileStatusEntry>> Failure(string message) =>
            Result<IReadOnlyList<FileStatusEntry>>.Failure(new LoreError(ErrorCode.InvalidRepository, message));
    }
}
