using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Operations
{
    public sealed class WorkingCopyStatusVerifier : IWorkingCopyStatusVerifier
    {
        private readonly ISerializedStatusBackend _backend;

        public WorkingCopyStatusVerifier(ISerializedStatusBackend backend)
        {
            _backend = backend ?? throw new ArgumentNullException(nameof(backend));
        }

        public async Task<Result> VerifyUnderLeaseAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            var result = await _backend.ReadUnderLeaseAsync(repository, Array.Empty<RepositoryPath>(), token);
            return result.IsSuccess ? Result.Success() : Result.Failure(new LoreError(ErrorCode.ValidationFailed,
                "Lore status could not be confirmed after the working copy write."));
        }
    }
}
