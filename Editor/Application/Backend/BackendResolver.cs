using System;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Backend
{
    public sealed class BackendResolver
    {
        private readonly IBackendCapabilityProvider _capabilities;

        public BackendResolver(IBackendCapabilityProvider capabilities)
        {
            _capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities));
        }

        public Result<OperationBackend> Resolve(BackendCapability capability)
        {
            if (!Enum.IsDefined(typeof(BackendCapability), capability))
                return Result<OperationBackend>.Failure(new LoreError(ErrorCode.UnsupportedOperation, "Unknown capability."));
            if (_capabilities.Supports(BackendKind.Sdk, capability))
                return Result<OperationBackend>.Success(new OperationBackend(BackendKind.Sdk, capability));
            if (_capabilities.Supports(BackendKind.Cli, capability))
                return Result<OperationBackend>.Success(new OperationBackend(BackendKind.Cli, capability));
            return Result<OperationBackend>.Failure(new LoreError(ErrorCode.UnsupportedOperation, "No backend supports this operation."));
        }
    }
}
