using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Status;
using Lore.Unity.Application.Queries;
using Lore.Unity.Application.Diff;
using Lore.Unity.Application.Conflicts;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.Backend;
using Lore.Unity.Infrastructure.LoreCli;
using Lore.Unity.Infrastructure.LoreSdk;
using Lore.Unity.Infrastructure.Recovery;
using Lore.Unity.Infrastructure.Repository;

namespace Lore.Unity.Integration.EditorLifecycle
{
    // Assemble per-project read services only after an executable or SDK is verified.
    // Missing adapters are not advertised. No static service locator is used.
    public sealed class ReadBackendComposition
    {
        private readonly CliReadAdapter _cliRead;
        private readonly LoreCliRunner _runner;
        private readonly RepositoryLocations _roots;
        private readonly IRepositoryOperationGate _gate;

        private ReadBackendComposition(BackendSession session, StatusStore store,
            CliReadAdapter cliRead, LoreCliRunner runner, RepositoryLocations roots, IRepositoryOperationGate gate)
        {
            Session = session;
            Status = new StatusReader(session, store);
            Store = store;
            _cliRead = cliRead;
            _runner = runner;
            _roots = roots;
            _gate = gate;
            Queries = runner != null && roots != null
                ? new RepositoryQueries(new CliRepositoryQueries(runner, roots)) : null;
            if (runner != null && roots != null && gate != null)
            {
                var source = new CliDiffAdapter(runner, roots, gate);
                Diff = new DiffService(source, new UnityYamlStructuredDiff(source));
            }
        }

        public BackendSession Session { get; }
        public StatusReader Status { get; }
        public StatusStore Store { get; }
        public RepositoryQueries Queries { get; }
        public DiffService Diff { get; }

        public Result<ConflictService> CreateConflictRecovery(IWorkingCopyGuard guard, FileRecoveryJournal journal)
        {
            if (guard == null || journal == null) throw new System.ArgumentNullException(
                guard == null ? nameof(guard) : nameof(journal));
            if (_cliRead == null || _runner == null || _roots == null || _gate == null)
                return Result<ConflictService>.Failure(new Lore.Unity.Core.Errors.LoreError(
                    Lore.Unity.Core.Errors.ErrorCode.UnsupportedOperation, "Verified CLI is required for conflict recovery."));
            var backend = new CliWriteAdapter(_runner, _roots, new CliWriteEventParser());
            return Result<ConflictService>.Success(new ConflictService(backend, _cliRead, _gate, guard, journal));
        }

        // Not invoked at Editor startup. A caller must provide safety/recovery
        // dependencies explicitly; an incomplete journal prevents activation.
        internal Result<WriteBackendComposition> CreateWrites(IWorkingCopyGuard guard, FileRecoveryJournal journal)
        {
            if (guard == null || journal == null) throw new System.ArgumentNullException(
                guard == null ? nameof(guard) : nameof(journal));
            if (_cliRead == null || _runner == null || _roots == null || _gate == null)
                return Result<WriteBackendComposition>.Failure(new Lore.Unity.Core.Errors.LoreError(
                    Lore.Unity.Core.Errors.ErrorCode.UnsupportedOperation, "No verified CLI write backend is available."));
            var pending = journal.Pending();
            if (pending.IsFailure) return Result<WriteBackendComposition>.Failure(pending.Error);
            if (pending.Value.Count != 0)
                return Result<WriteBackendComposition>.Failure(new Lore.Unity.Core.Errors.LoreError(
                    Lore.Unity.Core.Errors.ErrorCode.ValidationFailed, "Unresolved working copy operations block writes."));
            return Result<WriteBackendComposition>.Success(new WriteBackendComposition(_cliRead, _runner,
                _roots, _gate, guard, journal, Store));
        }

        public RepositoryDetector CreateDetector()
        {
            var backend = Session.ResolveRepository();
            return backend.IsSuccess ? new RepositoryDetector(backend.Value) : null;
        }

        public static ReadBackendComposition Create(ILoreSdkReadBridge sdkBridge,
            AbsolutePath? verifiedCliExecutable, IRepositoryOperationGate gate)
        {
            IBackendSet sdk = sdkBridge != null && sdkBridge.IsAvailable
                ? new ReadSet(new SdkReadAdapter(sdkBridge)) : null;
            LoreCliRunner runner = null;
            RepositoryLocations roots = null;
            CliReadAdapter cliRead = null;
            if (verifiedCliExecutable.HasValue && gate != null)
            {
                runner = new LoreCliRunner(verifiedCliExecutable.Value);
                roots = new RepositoryLocations();
                cliRead = new CliReadAdapter(runner, new CliStatusParser(), gate, roots);
            }
            IBackendSet cli = cliRead == null ? null : new ReadSet(cliRead);
            var provider = new AvailableCapabilities(sdk, cli);
            var session = new BackendSession(new BackendResolver(provider), sdk, cli);
            return new ReadBackendComposition(session, new StatusStore(), cliRead, runner, roots, gate);
        }

        private sealed class ReadSet : IBackendSet
        {
            public ReadSet(SdkReadAdapter adapter) { Repository = adapter; Status = adapter; }
            public ReadSet(CliReadAdapter adapter) { Repository = adapter; Status = adapter; }
            public IRepositoryBackend Repository { get; }
            public IStatusBackend Status { get; }
        }
    }
}
