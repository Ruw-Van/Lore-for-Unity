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

        private static LoreError Unavailable() =>
            new LoreError(ErrorCode.UnsupportedOperation, "Selected backend is not configured.");
    }
}
