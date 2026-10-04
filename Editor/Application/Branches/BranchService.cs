using System;
using System.Collections.Generic;
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
        private readonly IRepositoryOperationGate _gate;
        public BranchService(BackendSession backends, WorkingCopyOperation operation, IRepositoryOperationGate gate)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public async Task<Result<IReadOnlyList<BranchName>>> ListAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            var selected = _backends.ResolveBranch();
            if (selected.IsFailure) return Result<IReadOnlyList<BranchName>>.Failure(selected.Error);
            using (await _gate.AcquireAsync(repository, token))
                return await selected.Value.ListAsync(repository, token);
        }

        public Task<WriteOutcome> SwitchAsync(RepositoryId repository, BranchName branch, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (branch == null) throw new ArgumentNullException(nameof(branch));
            var selected = _backends.ResolveBranch();
            if (selected.IsFailure) return Task.FromResult(new WriteOutcome(WriteErrors.NewId(),
                Result.Failure(selected.Error), OperationState.Failed));
            return _operation.ExecuteAsync(repository, "BranchSwitch",
                ct => selected.Value.SwitchAsync(repository, branch, ct), token);
        }
    }
}
