using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Status
{
    public sealed class StatusReader
    {
        private readonly BackendSession _backends;
        private readonly StatusStore _store;

        public StatusReader(BackendSession backends, StatusStore store)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _store = store ?? throw new ArgumentNullException(nameof(store));
        }

        public async Task<Result<StatusSnapshot>> RefreshRepositoryAsync(RepositoryId repository,
            CancellationToken cancellationToken)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            var selected = _backends.ResolveStatus();
            if (selected.IsFailure) return Result<StatusSnapshot>.Failure(selected.Error);
            var generation = _store.BeginRefresh();
            var result = await selected.Value.ReadAsync(repository, new RepositoryPath[0], cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (result.IsFailure) return Result<StatusSnapshot>.Failure(result.Error);
            StatusSnapshot snapshot;
            try { snapshot = new StatusSnapshot(repository, generation, DateTime.UtcNow, result.Value); }
            catch (ArgumentException)
            {
                return Result<StatusSnapshot>.Failure(new LoreError(ErrorCode.ValidationFailed,
                    "Backend returned invalid status data."));
            }
            if (!_store.TryPublish(snapshot))
                return Result<StatusSnapshot>.Failure(new LoreError(ErrorCode.ValidationFailed,
                    "Stale status result was discarded."));
            return Result<StatusSnapshot>.Success(snapshot);
        }
    }
}
