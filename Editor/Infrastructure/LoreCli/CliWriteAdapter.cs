using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.LoreCli
{
    // These methods are invoked under the repository gate by Application services.
    // Only known CLI v0.10.0 commands are used; no --reset or implicit PATH lookup.
    public sealed class CliWriteAdapter : IRevisionBackend, IPushBackend, ISyncBackend, IBranchBackend,
        ILockBackend, IMergeBackend, IConflictBackend, Lore.Unity.Application.Conflicts.IEditedConflictBackend
    {
        private readonly LoreCliRunner _runner;
        private readonly RepositoryLocations _roots;
        private readonly CliWriteEventParser _parser;
        private readonly CliStatusParser _status = new CliStatusParser();
        private readonly CliMergeParser _merge = new CliMergeParser();
        private readonly CliResolveParser _resolve = new CliResolveParser();

        public CliWriteAdapter(LoreCliRunner runner, RepositoryLocations roots, CliWriteEventParser parser)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _roots = roots ?? throw new ArgumentNullException(nameof(roots));
            _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        }

        public async Task<Result> StageAsync(RepositoryId repository, IReadOnlyList<RepositoryPath> paths, CancellationToken token)
        {
            if (paths == null || paths.Count == 0) throw new ArgumentException("Paths required.", nameof(paths));
            var args = new List<string> { "--json", "--offline", "stage", "--" };
            foreach (var path in paths)
            {
                if (string.IsNullOrEmpty(path.Value)) throw new ArgumentException("Path required.", nameof(paths));
                args.Add(path.Value);
            }
            var output = await RunAsync(repository, args, token);
            return output.IsFailure ? Result.Failure(output.Error) : _parser.Verify(output.Value.StandardOutput);
        }

        public async Task<Result<RevisionSignature>> CreateAsync(RepositoryId repository, string message, CancellationToken token)
        {
            if (string.IsNullOrWhiteSpace(message)) throw new ArgumentException("Commit message required.", nameof(message));
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (!_roots.TryGet(repository, out var root))
                return Result<RevisionSignature>.Failure(new LoreError(ErrorCode.InvalidRepository,
                    "Repository has not been verified in this session."));
            var output = await _runner.RunAsync(root, new[] { "--json", "--offline", "commit", message }, token);
            if (output.IsFailure) return Result<RevisionSignature>.Failure(output.Error);
            if (output.Value.ExitCode == 0)
            {
                var complete = _parser.Commit(repository, output.Value.StandardOutput);
                if (complete.IsSuccess) return complete;
            }
            var candidate = _parser.LocalCommitCandidate(repository, output.Value.StandardOutput);
            if (candidate.IsFailure) return Result<RevisionSignature>.Failure(new LoreError(ErrorCode.Unknown,
                "Commit outcome is unknown; re-query Lore before retry."));
            var status = await _runner.RunAsync(root,
                new[] { "--json", "--offline", "status", "--revision-only" }, token);
            if (status.IsFailure || status.Value.ExitCode != 0)
                return Result<RevisionSignature>.Failure(new LoreError(ErrorCode.Unknown,
                    "Commit outcome could not be confirmed."));
            var parsed = _status.Parse(root, status.Value.StandardOutput);
            if (parsed.IsFailure || !parsed.Value.Repository.Id.Equals(repository) ||
                !parsed.Value.Repository.Revision.Equals(candidate.Value))
                return Result<RevisionSignature>.Failure(new LoreError(ErrorCode.Unknown,
                    "Commit revision did not match the current Lore state."));
            return candidate;
        }

        public async Task<Result> PushAsync(RepositoryId repository, CancellationToken token) =>
            await ExecuteAsync(repository, new[] { "--json", "push" }, token);

        public async Task<Result> SyncAsync(RepositoryId repository, CancellationToken token)
        {
            var output = await RunAsync(repository, new[] { "--json", "sync" }, token);
            return output.IsFailure ? Result.Failure(output.Error) : _parser.Sync(repository, output.Value.StandardOutput);
        }

        public async Task<Result> SwitchAsync(RepositoryId repository, BranchName branch, CancellationToken token)
        {
            if (branch == null) throw new ArgumentNullException(nameof(branch));
            var output = await RunAsync(repository,
                new[] { "--json", "branch", "switch", "--", branch.Value }, token);
            return output.IsFailure ? Result.Failure(output.Error) : _parser.BranchSwitch(branch, output.Value.StandardOutput);
        }

        public async Task<Result> MergeAsync(RepositoryId repository, BranchName source, CancellationToken token)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            var output = await RunAsync(repository,
                new[] { "--json", "--offline", "branch", "merge", "--", source.Value }, token);
            return output.IsFailure ? Result.Failure(output.Error) : _merge.Parse(output.Value.StandardOutput);
        }

        public async Task<Result> ChooseVersionAsync(RepositoryId repository,
            IReadOnlyList<RepositoryPath> paths, ConflictChoice choice, CancellationToken token)
        {
            if (paths == null || paths.Count == 0) throw new ArgumentException("Paths required.", nameof(paths));
            if (choice != ConflictChoice.Mine && choice != ConflictChoice.Theirs)
                throw new ArgumentOutOfRangeException(nameof(choice));
            var args = new List<string> { "--json", "--offline", "branch", "merge", "resolve",
                choice == ConflictChoice.Mine ? "mine" : "theirs", "--" };
            foreach (var path in paths) args.Add(path.Value);
            var output = await RunAsync(repository, args, token);
            return output.IsFailure ? Result.Failure(output.Error) :
                _resolve.Parse(repository, paths, output.Value.StandardOutput);
        }

        public async Task<Result> StageAndResolveAsync(RepositoryId repository, RepositoryPath path,
            CancellationToken token)
        {
            var paths = new[] { path };
            var staged = await StageAsync(repository, paths, token);
            if (staged.IsFailure) return staged;
            var output = await RunAsync(repository,
                new[] { "--json", "--offline", "branch", "merge", "resolve", "--", path.Value }, token);
            return output.IsFailure ? Result.Failure(output.Error) :
                _resolve.Parse(repository, paths, output.Value.StandardOutput);
        }

        public async Task<Result<IReadOnlyList<BranchName>>> ListAsync(RepositoryId repository, CancellationToken token)
        {
            var output = await RunAsync(repository, new[] { "--json", "--offline", "branch", "list" }, token);
            return output.IsFailure ? Result<IReadOnlyList<BranchName>>.Failure(output.Error) :
                _parser.Branches(output.Value.StandardOutput);
        }

        public Task<Result> AcquireAsync(RepositoryId repository, RepositoryPath path, CancellationToken token)
        {
            if (string.IsNullOrEmpty(path.Value)) throw new ArgumentException("Path required.", nameof(path));
            return ExecuteAsync(repository, new[] { "--json", "lock", "acquire", "--", path.Value }, token);
        }

        public Task<Result> ReleaseAsync(RepositoryId repository, RepositoryPath path, CancellationToken token)
        {
            if (string.IsNullOrEmpty(path.Value)) throw new ArgumentException("Path required.", nameof(path));
            return ExecuteAsync(repository, new[] { "--json", "lock", "release", "--", path.Value }, token);
        }

        private async Task<Result> ExecuteAsync(RepositoryId repository, string[] args, CancellationToken token)
        {
            var output = await RunAsync(repository, args, token);
            return output.IsFailure ? Result.Failure(output.Error) : _parser.Verify(output.Value.StandardOutput);
        }

        private async Task<Result<CliOutput>> RunAsync(RepositoryId repository,
            IReadOnlyList<string> args, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (!_roots.TryGet(repository, out var root))
                return Result<CliOutput>.Failure(new LoreError(ErrorCode.InvalidRepository,
                    "Repository has not been verified in this session."));
            var output = await _runner.RunAsync(root, args, token);
            if (output.IsFailure) return output;
            if (output.Value.ExitCode != 0)
                return Result<CliOutput>.Failure(new LoreError(ErrorCode.Unknown,
                    "Lore CLI operation failed; re-query Lore before retry."));
            return output;
        }
    }
}
