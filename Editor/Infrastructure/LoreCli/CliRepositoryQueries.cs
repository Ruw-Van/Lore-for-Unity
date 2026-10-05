using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Queries;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.LoreCli
{
    public sealed class CliRepositoryQueries : IRepositoryQueryBackend
    {
        private readonly LoreCliRunner _runner;
        private readonly RepositoryLocations _roots;
        private readonly CliWriteEventParser _branches = new CliWriteEventParser();
        private readonly CliHistoryParser _history = new CliHistoryParser();

        public CliRepositoryQueries(LoreCliRunner runner, RepositoryLocations roots)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _roots = roots ?? throw new ArgumentNullException(nameof(roots));
        }

        public async Task<Result<IReadOnlyList<BranchName>>> ListBranchesAsync(RepositoryId repository,
            CancellationToken token)
        {
            var output = await RunAsync(repository, new[] { "--json", "--offline", "branch", "list" }, token);
            return output.IsFailure ? Result<IReadOnlyList<BranchName>>.Failure(output.Error) :
                _branches.Branches(output.Value.StandardOutput);
        }

        public async Task<Result<IReadOnlyList<RevisionHistoryEntry>>> HistoryAsync(RepositoryId repository,
            int limit, CancellationToken token)
        {
            if (limit < 1 || limit > 200) throw new ArgumentOutOfRangeException(nameof(limit));
            var output = await RunAsync(repository, new[] { "--json", "--offline", "history",
                limit.ToString(CultureInfo.InvariantCulture) }, token);
            // v0.10.0 may emit a local complete:0 followed by a relay error and
            // still exit 0. The history parser validates both completions.
            return output.IsFailure ? Result<IReadOnlyList<RevisionHistoryEntry>>.Failure(output.Error) :
                _history.Parse(repository, output.Value.StandardOutput);
        }

        private async Task<Result<CliOutput>> RunAsync(RepositoryId repository,
            IReadOnlyList<string> arguments, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (!_roots.TryGet(repository, out var root))
                return Result<CliOutput>.Failure(new LoreError(ErrorCode.InvalidRepository,
                    "Repository has not been verified in this session."));
            var output = await _runner.RunAsync(root, arguments, token);
            if (output.IsFailure) return output;
            if (output.Value.ExitCode != 0) return Result<CliOutput>.Failure(new LoreError(ErrorCode.Unknown,
                "Lore CLI repository query failed."));
            return output;
        }
    }
}
