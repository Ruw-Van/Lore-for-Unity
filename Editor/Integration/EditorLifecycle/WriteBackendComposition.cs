using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Branches;
using Lore.Unity.Application.CheckIn;
using Lore.Unity.Application.Locks;
using Lore.Unity.Application.Merge;
using Lore.Unity.Application.Operations;
using Lore.Unity.Application.Push;
using Lore.Unity.Application.Status;
using Lore.Unity.Application.Sync;
using Lore.Unity.Infrastructure.Backend;
using Lore.Unity.Infrastructure.LoreCli;
using Lore.Unity.Infrastructure.Recovery;

namespace Lore.Unity.Integration.EditorLifecycle
{
    public sealed class WriteBackendComposition
    {
        internal WriteBackendComposition(CliReadAdapter read, LoreCliRunner runner,
            RepositoryLocations roots, IRepositoryOperationGate gate, IWorkingCopyGuard guard,
            FileRecoveryJournal journal, StatusStore store)
        {
            var write = new CliWriteAdapter(runner, roots, new CliWriteEventParser());
            var set = new CliSet(read, write);
            var session = new BackendSession(new BackendResolver(new AvailableCapabilities(null, set)), null, set);
            var workingCopy = new WorkingCopyOperation(gate, guard, journal, new WorkingCopyStatusVerifier(read));
            CheckIn = new CheckInService(session, gate, guard);
            StageSelection = new StageSelectionService(session, gate, guard);
            Push = new PushService(session, gate);
            Sync = new SyncService(session, workingCopy);
            Branch = new BranchService(session, workingCopy, gate);
            Lock = new LockService(session, gate);
            Merge = new MergeService(session, workingCopy);
            Status = new StatusReader(session, store);
        }

        public CheckInService CheckIn { get; }
        public StageSelectionService StageSelection { get; }
        public PushService Push { get; }
        public SyncService Sync { get; }
        public BranchService Branch { get; }
        public LockService Lock { get; }
        public MergeService Merge { get; }
        public StatusReader Status { get; }

        private sealed class CliSet : IWriteBackendSet
        {
            public CliSet(CliReadAdapter read, CliWriteAdapter write)
            {
                Repository = read;
                Status = read;
                Revision = write;
                Push = write;
                Sync = write;
                Branch = write;
                Lock = write;
                Merge = write;
            }
            public IRepositoryBackend Repository { get; }
            public IStatusBackend Status { get; }
            public IRevisionBackend Revision { get; }
            public IPushBackend Push { get; }
            public ISyncBackend Sync { get; }
            public IBranchBackend Branch { get; }
            public ILockBackend Lock { get; }
            public IMergeBackend Merge { get; }
        }
    }
}
