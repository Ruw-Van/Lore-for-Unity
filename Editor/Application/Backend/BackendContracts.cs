using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Repository;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Status;

namespace Lore.Unity.Application.Backend
{
    public enum BackendKind { Sdk, Cli }
    public enum BackendCapability { Repository, Status, Revision, Push, Branch, Lock, Diff, Merge }

    // Resolve once before an operation. Never switch backends after a side effect.
    public interface IBackendCapabilityProvider
    {
        bool Supports(BackendKind backend, BackendCapability capability);
    }

    public sealed class OperationBackend
    {
        public OperationBackend(BackendKind kind, BackendCapability capability)
        {
            Kind = kind;
            Capability = capability;
        }
        public BackendKind Kind { get; }
        public BackendCapability Capability { get; }
    }

    public interface IRepositoryBackend
    {
        Task<Result<RepositorySnapshot>> DetectAsync(AbsolutePath projectRoot, CancellationToken cancellationToken);
    }

    public sealed class FileStatusEntry
    {
        public FileStatusEntry(RepositoryPath path, FileStatus status)
        {
            if (string.IsNullOrEmpty(path.Value)) throw new System.ArgumentException("A path is required.", nameof(path));
            Path = path;
            Status = status;
        }
        public RepositoryPath Path { get; }
        public FileStatus Status { get; }
    }

    public interface IStatusBackend
    {
        Task<Result<IReadOnlyList<FileStatusEntry>>> ReadAsync(RepositoryId repository,
            IReadOnlyList<RepositoryPath> paths, CancellationToken cancellationToken);
    }

    public interface IRevisionBackend
    {
        Task<Result> StageAsync(RepositoryId repository, IReadOnlyList<RepositoryPath> paths, CancellationToken cancellationToken);
        Task<Result<RevisionSignature>> CreateAsync(RepositoryId repository, string message, CancellationToken cancellationToken);
    }

    public interface IPushBackend
    {
        Task<Result> PushAsync(RepositoryId repository, CancellationToken cancellationToken);
    }

    public interface IBranchBackend
    {
        Task<Result<IReadOnlyList<BranchName>>> ListAsync(RepositoryId repository, CancellationToken cancellationToken);
        Task<Result> SwitchAsync(RepositoryId repository, BranchName branch, CancellationToken cancellationToken);
    }

    public interface ILockBackend
    {
        Task<Result> AcquireAsync(RepositoryId repository, RepositoryPath path, CancellationToken cancellationToken);
        Task<Result> ReleaseAsync(RepositoryId repository, RepositoryPath path, CancellationToken cancellationToken);
    }

    public interface IDiffBackend
    {
        Task<Result<string>> ReadTextAsync(RepositoryId repository, RepositoryPath path, CancellationToken cancellationToken);
    }

    public interface IMergeBackend
    {
        Task<Result> MergeAsync(RepositoryId repository, BranchName branch, CancellationToken cancellationToken);
    }
}
