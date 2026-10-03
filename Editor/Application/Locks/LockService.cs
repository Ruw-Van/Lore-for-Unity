using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Operations;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Locks
{
    public sealed class LockService
    {
        private readonly BackendSession _backends;
        private readonly IRepositoryOperationGate _gate;

        public LockService(BackendSession backends, IRepositoryOperationGate gate)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public Task<WriteOutcome> AcquireAsync(RepositoryId repository, RepositoryPath path, CancellationToken token) =>
            ExecuteAsync(repository, path, false, token);

        public Task<WriteOutcome> ReleaseAsync(RepositoryId repository, RepositoryPath path, CancellationToken token) =>
            ExecuteAsync(repository, path, true, token);

        private async Task<WriteOutcome> ExecuteAsync(RepositoryId repository, RepositoryPath path,
            bool release, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (string.IsNullOrEmpty(path.Value)) throw new ArgumentException("Path required.", nameof(path));
            var id = WriteErrors.NewId();
            var selected = _backends.ResolveLock();
            if (selected.IsFailure) return new WriteOutcome(id, Result.Failure(selected.Error), OperationState.Failed);
            using (await _gate.AcquireAsync(repository, token))
            {
                try
                {
                    var result = release ? await selected.Value.ReleaseAsync(repository, path, token) :
                        await selected.Value.AcquireAsync(repository, path, token);
                    return new WriteOutcome(id, result, result.IsSuccess ? OperationState.Completed : OperationState.Failed);
                }
                catch (OperationCanceledException)
                {
                    return new WriteOutcome(id, WriteErrors.Failure(ErrorCode.Unknown,
                        "Lock outcome must be re-queried before retry."), OperationState.Cancelled);
                }
            }
        }
    }
}
