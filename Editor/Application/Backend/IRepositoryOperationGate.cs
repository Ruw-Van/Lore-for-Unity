using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Identifiers;

namespace Lore.Unity.Application.Backend
{
    // Status --scan is a Lore write. Share this gate with future working-copy operations.
    public interface IRepositoryOperationGate
    {
        Task<IDisposable> AcquireAsync(RepositoryId repository, CancellationToken cancellationToken);
    }
}
