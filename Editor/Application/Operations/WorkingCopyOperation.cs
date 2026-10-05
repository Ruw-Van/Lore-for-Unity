using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Operations;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Operations
{
    // Shared safety boundary for Sync and Branch Switch. Lore's success is not
    // Unity's success: post-import validation and journal finalization come after it.
    public sealed class WorkingCopyOperation
    {
        private readonly IRepositoryOperationGate _gate;
        private readonly IWorkingCopyGuard _guard;
        private readonly IRecoveryJournal _journal;
        private readonly IWorkingCopyStatusVerifier _status;

        public WorkingCopyOperation(IRepositoryOperationGate gate, IWorkingCopyGuard guard,
            IRecoveryJournal journal, IWorkingCopyStatusVerifier status)
        {
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
            _guard = guard ?? throw new ArgumentNullException(nameof(guard));
            _journal = journal ?? throw new ArgumentNullException(nameof(journal));
            _status = status ?? throw new ArgumentNullException(nameof(status));
        }

        public async Task<WriteOutcome> ExecuteAsync(RepositoryId repository, string operation,
            Func<CancellationToken, Task<Result>> loreWrite, CancellationToken token,
            bool retainOnConflict = false)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (loreWrite == null) throw new ArgumentNullException(nameof(loreWrite));
            var id = WriteErrors.NewId();
            using (await _gate.AcquireAsync(repository, token))
            {
                var preflight = await _guard.ValidateBeforeWriteAsync(repository, token);
                if (preflight.IsFailure) return new WriteOutcome(id, preflight, OperationState.Failed);
                var recorded = await _journal.BeginAsync(id, repository, operation, token);
                if (recorded.IsFailure) return new WriteOutcome(id, recorded, OperationState.Failed);
                Result lore;
                try { lore = await loreWrite(token); }
                catch (OperationCanceledException)
                {
                    return new WriteOutcome(id, WriteErrors.Failure(ErrorCode.Unknown,
                        "Lore write outcome is unknown; inspect recovery journal before retry."), OperationState.Cancelled);
                }
                var conflict = retainOnConflict && lore.IsFailure && lore.Error.Code == ErrorCode.Conflict;
                if (lore.IsFailure && !conflict) return new WriteOutcome(id, lore, OperationState.Failed);
                // Once Lore has changed the working copy, finish safety checks even if
                // the UI cancelled; retain the journal if finalization fails.
                var marked = await _journal.LoreAppliedAsync(id, CancellationToken.None);
                if (marked.IsFailure) return new WriteOutcome(id, marked, OperationState.Failed, true);
                var unity = await _guard.ValidateAfterWriteAsync(repository, CancellationToken.None);
                if (unity.IsFailure) return new WriteOutcome(id, unity, OperationState.Failed, true);
                var verified = await _status.VerifyUnderLeaseAsync(repository, CancellationToken.None);
                if (verified.IsFailure) return new WriteOutcome(id, verified, OperationState.Failed, true);
                // Conflict is native Lore state. Keep the recovery record until
                // the user resolves or aborts it; do not mark the merge complete.
                if (conflict) return new WriteOutcome(id, lore, OperationState.Failed, true);
                var complete = await _journal.CompleteAsync(id, CancellationToken.None);
                return new WriteOutcome(id, complete,
                    complete.IsSuccess ? OperationState.Completed : OperationState.Failed, true);
            }
        }
    }
}
