using System;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Backend
{
    public interface IBackendSet
    {
        IRepositoryBackend Repository { get; }
        IStatusBackend Status { get; }
    }

    public interface IWriteBackendSet : IBackendSet
    {
        IRevisionBackend Revision { get; }
        IPushBackend Push { get; }
        ISyncBackend Sync { get; }
        IBranchBackend Branch { get; }
        ILockBackend Lock { get; }
        IMergeBackend Merge { get; }
    }

    // Binding is selected once; a failed call never triggers another backend.
    public sealed class BackendSession
    {
        private readonly BackendResolver _resolver;
        private readonly IBackendSet _sdk;
        private readonly IBackendSet _cli;

        public BackendSession(BackendResolver resolver, IBackendSet sdk, IBackendSet cli)
        {
            _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
            _sdk = sdk;
            _cli = cli;
        }

        public Result<IRepositoryBackend> ResolveRepository()
        {
            var resolved = _resolver.Resolve(BackendCapability.Repository);
            if (resolved.IsFailure) return Result<IRepositoryBackend>.Failure(resolved.Error);
            var backend = resolved.Value.Kind == BackendKind.Sdk ? _sdk?.Repository : _cli?.Repository;
            return backend == null ? Result<IRepositoryBackend>.Failure(Unavailable()) :
                Result<IRepositoryBackend>.Success(backend);
        }

        public Result<IStatusBackend> ResolveStatus()
        {
            var resolved = _resolver.Resolve(BackendCapability.Status);
            if (resolved.IsFailure) return Result<IStatusBackend>.Failure(resolved.Error);
            var backend = resolved.Value.Kind == BackendKind.Sdk ? _sdk?.Status : _cli?.Status;
            return backend == null ? Result<IStatusBackend>.Failure(Unavailable()) :
                Result<IStatusBackend>.Success(backend);
        }

        public Result<IRevisionBackend> ResolveRevision() => ResolveWrite(BackendCapability.Revision, set => set.Revision);
        public Result<IPushBackend> ResolvePush() => ResolveWrite(BackendCapability.Push, set => set.Push);
        public Result<ISyncBackend> ResolveSync() => ResolveWrite(BackendCapability.Sync, set => set.Sync);
        public Result<IBranchBackend> ResolveBranch() => ResolveWrite(BackendCapability.Branch, set => set.Branch);
        public Result<ILockBackend> ResolveLock() => ResolveWrite(BackendCapability.Lock, set => set.Lock);
        public Result<IMergeBackend> ResolveMerge() => ResolveWrite(BackendCapability.Merge, set => set.Merge);

        private Result<T> ResolveWrite<T>(BackendCapability capability, Func<IWriteBackendSet, T> pick) where T : class
        {
            var resolved = _resolver.Resolve(capability);
            if (resolved.IsFailure) return Result<T>.Failure(resolved.Error);
            var set = (resolved.Value.Kind == BackendKind.Sdk ? _sdk : _cli) as IWriteBackendSet;
            var backend = set == null ? null : pick(set);
            return backend == null ? Result<T>.Failure(Unavailable()) : Result<T>.Success(backend);
        }

        private static LoreError Unavailable() =>
            new LoreError(ErrorCode.UnsupportedOperation, "Selected backend is not configured.");
    }
}
