using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Operations;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Sync
{
    public sealed class SyncService
    {
        private readonly BackendSession _backends;
        private readonly WorkingCopyOperation _operation;
        public SyncService(BackendSession backends, WorkingCopyOperation operation)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        }

        public Task<WriteOutcome> ExecuteAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            var selected = _backends.ResolveSync();
            if (selected.IsFailure) return Task.FromResult(new WriteOutcome(WriteErrors.NewId(),
                Result.Failure(selected.Error), OperationState.Failed));
            return _operation.ExecuteAsync(repository, "Sync", ct => selected.Value.SyncAsync(repository, ct), token);
        }
    }
}
