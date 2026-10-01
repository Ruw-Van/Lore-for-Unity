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
using Lore.Unity.Core.Status;

namespace Lore.Unity.Infrastructure.LoreSdk
{
    // Boundary for a version-verified, Unity-compatible SDK bridge. The public LoreVcs
    // package currently targets net9/net10 and must not be pulled into Unity asmdefs.
    public interface ILoreSdkReadBridge
    {
        Task<Result<SdkRepositoryData>> ReadRepositoryAsync(AbsolutePath root, CancellationToken cancellationToken);
        Task<Result<IReadOnlyList<SdkFileData>>> ReadStatusAsync(RepositoryId repository,
            IReadOnlyList<RepositoryPath> paths, CancellationToken cancellationToken);
    }

    public sealed class SdkRepositoryData
    {
        public string Id;
        public string Root;
        public string Branch;
        public string Revision;
    }

    public sealed class SdkFileData
    {
        public string Path;
        public WorkingState Working;
        public StageState Stage;
        public LockState Lock;
        public ConflictState Conflict;
        public RemoteState Remote;
    }

    public sealed class SdkReadAdapter : IRepositoryBackend, IStatusBackend
    {
        private readonly ILoreSdkReadBridge _bridge;

        public SdkReadAdapter(ILoreSdkReadBridge bridge)
        {
            _bridge = bridge ?? throw new ArgumentNullException(nameof(bridge));
        }

        public async Task<Result<RepositorySnapshot>> DetectAsync(AbsolutePath projectRoot,
            CancellationToken cancellationToken)
        {
            var result = await _bridge.ReadRepositoryAsync(projectRoot, cancellationToken);
            if (result.IsFailure) return Result<RepositorySnapshot>.Failure(result.Error);
            var data = result.Value;
            try
            {
                if (data == null || string.IsNullOrWhiteSpace(data.Id) || string.IsNullOrWhiteSpace(data.Root))
                    throw new ArgumentException("Missing repository identity.");
                return Result<RepositorySnapshot>.Success(new RepositorySnapshot(new RepositoryId(data.Id),
                    new AbsolutePath(data.Root), string.IsNullOrWhiteSpace(data.Branch) ? null : new BranchName(data.Branch),
                    string.IsNullOrWhiteSpace(data.Revision) ? null : new RevisionSignature(data.Revision)));
            }
            catch (ArgumentException)
            {
                return Result<RepositorySnapshot>.Failure(new LoreError(ErrorCode.InvalidRepository,
                    "Lore returned invalid repository data."));
            }
        }

        public async Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId repository,
            IReadOnlyList<RepositoryPath> paths, CancellationToken cancellationToken)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            var result = await _bridge.ReadStatusAsync(repository, paths, cancellationToken);
            if (result.IsFailure) return Result<IReadOnlyList<FileStatusEntry>>.Failure(result.Error);
            if (result.Value == null)
                return InvalidStatus();
            var output = new List<FileStatusEntry>();
            var seen = new HashSet<RepositoryPath>();
            try
            {
                foreach (var data in result.Value)
                {
                    if (data == null || !Enum.IsDefined(typeof(WorkingState), data.Working) ||
                        !Enum.IsDefined(typeof(StageState), data.Stage) ||
                        !Enum.IsDefined(typeof(LockState), data.Lock) ||
                        !Enum.IsDefined(typeof(ConflictState), data.Conflict) ||
                        !Enum.IsDefined(typeof(RemoteState), data.Remote)) return InvalidStatus();
                    var path = new RepositoryPath(data.Path);
                    if (!seen.Add(path)) return InvalidStatus();
                    output.Add(new FileStatusEntry(path,
                        new FileStatus(data.Working, data.Stage, data.Lock, data.Conflict, data.Remote)));
                }
            }
            catch (ArgumentException) { return InvalidStatus(); }
            return Result<IReadOnlyList<FileStatusEntry>>.Success(output.AsReadOnly());
        }

        private static Result<IReadOnlyList<FileStatusEntry>> InvalidStatus() =>
            Result<IReadOnlyList<FileStatusEntry>>.Failure(new LoreError(ErrorCode.ValidationFailed,
                "Lore returned invalid status data."));
    }
}
