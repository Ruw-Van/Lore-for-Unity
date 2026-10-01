using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Identifiers;

namespace Lore.Unity.Infrastructure.Backend
{
    public sealed class RepositoryOperationGate : IRepositoryOperationGate
    {
        private readonly ConcurrentDictionary<RepositoryId, SemaphoreSlim> _gates =
            new ConcurrentDictionary<RepositoryId, SemaphoreSlim>();

        public async Task<IDisposable> AcquireAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            var gate = _gates.GetOrAdd(repository, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync(token);
            return new Lease(gate);
        }

        private sealed class Lease : IDisposable
        {
            private SemaphoreSlim _semaphore;
            public Lease(SemaphoreSlim semaphore) { _semaphore = semaphore; }
            public void Dispose() { Interlocked.Exchange(ref _semaphore, null)?.Release(); }
        }
    }
}
