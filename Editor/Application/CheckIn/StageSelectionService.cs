using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Status;

namespace Lore.Unity.Application.CheckIn
{
    public sealed class StageSelectionProgress
    {
        public StageSelectionProgress(int completed, int total, RepositoryPath current)
        { Completed = completed; Total = total; Current = current; }
        public int Completed { get; }
        public int Total { get; }
        public RepositoryPath Current { get; }
    }

    // Stage is deliberately separate from Commit. A failed/cancelled batch can
    // leave partial native stage state; a later call re-reads it before resuming.
    public sealed class StageSelectionService
    {
        private const int MaxArguments = 12000;
        private const int MaxPathsPerBatch = 100;
        private readonly BackendSession _backends;
        private readonly IRepositoryOperationGate _gate;
        private readonly IWorkingCopyGuard _guard;

        public StageSelectionService(BackendSession backends, IRepositoryOperationGate gate, IWorkingCopyGuard guard)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
        }

        public async Task<Result> StageAsync(RepositoryId repository, IReadOnlyList<RepositoryPath> paths,
            string assetRootPrefix, IProgress<StageSelectionProgress> progress, CancellationToken token)
        {
            if (repository == null || paths == null || paths.Count == 0 || string.IsNullOrEmpty(assetRootPrefix))
                return Failure("Select changed assets before staging.");
            var revision = _backends.ResolveRevision();
            var status = _backends.ResolveStatus();
            if (revision.IsFailure || status.IsFailure)
                return Result.Failure(revision.IsFailure ? revision.Error : status.Error);
            using (await _gate.AcquireAsync(repository, token))
            {
                var safe = await _guard.ValidateBeforeWriteAsync(repository, token);
                if (safe.IsFailure) return safe;
                var selected = new HashSet<RepositoryPath>();
                foreach (var path in paths)
                {
                    if (string.IsNullOrEmpty(path.Value)) return Failure("Invalid selected path.");
                    var logical = Logical(path, assetRootPrefix);
                    if (!selected.Add(logical)) return Failure("Duplicate asset selection.");
                }
                var before = await Read(status.Value, repository, token);
                if (before.IsFailure) return Result.Failure(before.Error);
                // A directory CLI argument stages every descendant, including files
                // the user did not select. Never substitute it for explicit paths.
                var ancestors = new HashSet<RepositoryPath>();
                foreach (var entry in before.Value)
                    for (var slash = entry.Path.Value.IndexOf('/'); slash >= 0;
                         slash = entry.Path.Value.IndexOf('/', slash + 1))
                        ancestors.Add(new RepositoryPath(entry.Path.Value.Substring(0, slash)));
                foreach (var path in selected)
                    if (ancestors.Contains(path))
                        return Failure("Directory selection would stage other files. Select individual assets instead.");
                var groups = new SortedDictionary<string, List<FileStatusEntry>>(StringComparer.Ordinal);
                foreach (var entry in before.Value)
                {
                    var logical = Logical(entry.Path, assetRootPrefix);
                    if (entry.Status.Stage == StageState.Staged && !selected.Contains(logical) &&
                        !IsAncestorOfSelection(entry.Path, selected))
                        return Failure("Unrelated staged changes must be resolved before staging this selection.");
                    if (!selected.Contains(logical)) continue;
                    if (entry.Status.Conflict != ConflictState.None)
                        return Failure("Resolve Lore conflicts before staging.");
                    if (!groups.TryGetValue(logical.Value, out var group))
                        groups.Add(logical.Value, group = new List<FileStatusEntry>());
                    group.Add(entry);
                }
                if (groups.Count != selected.Count)
                    return Failure("A selected asset is no longer in Lore status. Refresh before staging.");
                var completed = 0;
                var batch = new List<RepositoryPath>();
                var batchGroups = 0;
                var length = 0;
                RepositoryPath last = default;
                progress?.Report(new StageSelectionProgress(0, groups.Count, last));
                foreach (var group in groups)
                {
                    var pending = new List<RepositoryPath>();
                    foreach (var entry in group.Value)
                        if (entry.Status.Stage != StageState.Staged)
                        {
                            if (entry.Status.Working == WorkingState.Unchanged)
                                return Failure("A selected asset is no longer changed. Refresh before staging.");
                            pending.Add(entry.Path);
                        }
                    var extra = 0;
                    foreach (var path in pending) extra += path.Value.Length * 2 + 4;
                    if (extra > MaxArguments) return Failure("A selected path is too long to stage safely.");
                    if (batch.Count > 0 && (batch.Count + pending.Count > MaxPathsPerBatch ||
                                            length + extra > MaxArguments))
                    {
                        var staged = await StageBatch(revision.Value, repository, batch, token);
                        if (staged.IsFailure) return staged;
                        completed += batchGroups;
                        progress?.Report(new StageSelectionProgress(completed, groups.Count, last));
                        batch.Clear();
                        batchGroups = 0;
                        length = 0;
                    }
                    last = new RepositoryPath(group.Key);
                    if (pending.Count == 0)
                    {
                        completed++;
                        progress?.Report(new StageSelectionProgress(completed, groups.Count, last));
                        continue;
                    }
                    batch.AddRange(pending);
                    length += extra;
                    batchGroups++;
                }
                if (batch.Count > 0)
                {
                    var staged = await StageBatch(revision.Value, repository, batch, token);
                    if (staged.IsFailure) return staged;
                    completed += batchGroups;
                    progress?.Report(new StageSelectionProgress(completed, groups.Count, last));
                }
                var after = await Read(status.Value, repository, token);
                if (after.IsFailure) return Result.Failure(after.Error);
                var verified = new HashSet<RepositoryPath>();
                foreach (var entry in after.Value)
                {
                    var logical = Logical(entry.Path, assetRootPrefix);
                    if (entry.Status.Stage == StageState.Staged)
                    {
                        if (!selected.Contains(logical))
                        {
                            if (!IsAncestorOfSelection(entry.Path, selected))
                                return Failure("Unrelated changes were staged. Inspect Lore state.");
                            continue;
                        }
                        verified.Add(logical);
                    }
                    else if (selected.Contains(logical))
                        return Failure("Some selected files remain unstaged. Inspect Lore state.");
                }
                return verified.SetEquals(selected) ? Result.Success() :
                    Failure("Stage verification was incomplete. Inspect Lore state.");
            }
        }

        private static async Task<Result> StageBatch(IRevisionBackend revision, RepositoryId repository,
            IReadOnlyList<RepositoryPath> batch, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            var result = await revision.StageAsync(repository, batch, token);
            return result.IsFailure ? Failure("Stage may be partial; refresh Lore status before retrying: " +
                result.Error.Message) : result;
        }

        public static RepositoryPath Logical(RepositoryPath path, string prefix) =>
            path.Value.StartsWith(prefix, StringComparison.Ordinal) &&
            path.Value.EndsWith(".meta", StringComparison.Ordinal)
                ? new RepositoryPath(path.Value.Substring(0, path.Value.Length - 5)) : path;

        public static bool IsAncestorOfSelection(RepositoryPath path, HashSet<RepositoryPath> selected)
        {
            var prefix = path.Value.TrimEnd('/') + "/";
            foreach (var candidate in selected)
                if (candidate.Value.StartsWith(prefix, StringComparison.Ordinal)) return true;
            return false;
        }

        private static Task<Result<IReadOnlyList<FileStatusEntry>>> Read(IStatusBackend status,
            RepositoryId repository, CancellationToken token) =>
            status is ISerializedStatusBackend serialized ?
                serialized.ReadUnderLeaseAsync(repository, Array.Empty<RepositoryPath>(), token) :
                status.ReadAsync(repository, Array.Empty<RepositoryPath>(), token);

        private static Result Failure(string message) => Result.Failure(new LoreError(ErrorCode.ValidationFailed, message));
    }
}
