using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Operations;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Merge
{
    public sealed class MergeService
    {
        private readonly BackendSession _backends;
        private readonly WorkingCopyOperation _operation;

        public MergeService(BackendSession backends, WorkingCopyOperation operation)
        {
            _backends = backends ?? throw new ArgumentNullException(nameof(backends));
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
        }

        public Task<WriteOutcome> ExecuteAsync(RepositoryId repository, BranchName source, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (source == null) throw new ArgumentNullException(nameof(source));
            var selected = _backends.ResolveMerge();
            if (selected.IsFailure) return Task.FromResult(new WriteOutcome(WriteErrors.NewId(),
                Result.Failure(selected.Error), OperationState.Failed));
            return _operation.ExecuteAsync(repository, "BranchMerge",
                ct => selected.Value.MergeAsync(repository, source, ct), token, retainOnConflict: true);
        }
    }
}
