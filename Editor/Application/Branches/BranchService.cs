using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Operations;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Branches
{
    public sealed class BranchService
    {
        private readonly BackendSession _backends;
        private readonly WorkingCopyOperation _operation;
        public BranchService(BackendSession backends, WorkingCopyOperation operation)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        }

        public Task<WriteOutcome> SwitchAsync(RepositoryId repository, BranchName branch, CancellationToken token)
        {
            if (branch == null) throw new ArgumentNullException(nameof(branch));
            var selected = _backends.ResolveBranch();
            if (selected.IsFailure) return Task.FromResult(new WriteOutcome(WriteErrors.NewId(),
                Result.Failure(selected.Error), OperationState.Failed));
            return _operation.ExecuteAsync(repository, "BranchSwitch",
                ct => selected.Value.SwitchAsync(repository, branch, ct), token);
        }
    }
}
