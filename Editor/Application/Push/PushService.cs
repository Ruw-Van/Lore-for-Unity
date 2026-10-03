using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Operations;

namespace Lore.Unity.Application.Push
{
    public sealed class PushService
    {
        private readonly BackendSession _backends;
        private readonly IRepositoryOperationGate _gate;

        public PushService(BackendSession backends, IRepositoryOperationGate gate)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public async Task<WriteOutcome> ExecuteAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            var id = WriteErrors.NewId();
            var selected = _backends.ResolvePush();
            if (selected.IsFailure) return new WriteOutcome(id,
                Lore.Unity.Core.Results.Result.Failure(selected.Error), OperationState.Failed);
            using (await _gate.AcquireAsync(repository, token))
            {
                try
                {
                    var result = await selected.Value.PushAsync(repository, token);
                    return new WriteOutcome(id, result, result.IsSuccess ? OperationState.Completed : OperationState.Failed);
                }
                catch (OperationCanceledException)
                {
                    return new WriteOutcome(id, WriteErrors.Failure(ErrorCode.Unknown,
                        "Push was cancelled; remote outcome must be checked before retry."), OperationState.Cancelled);
                }
            }
        }
    }
}
