namespace Lore.Unity.Core.Status
{
    public enum WorkingState
    {
        Unchanged = 0,
        Added,
        Modified,
        Deleted,
        Moved,
        Untracked
    }

    public enum StageState
    {
        Unstaged = 0,
        Staged,
        PartiallyStaged
    }

    public enum LockState
    {
        Unlocked = 0,
        LockedByCurrentUser,
        LockedByOther,
        LockPending
    }

    public enum ConflictState
    {
        None = 0,
        Conflicted,
        Resolving,
        Resolved
    }

    public enum RemoteState
    {
        Unknown = 0,
        UpToDate,
        Ahead,
        Behind,
        Diverged
    }

    public readonly struct FileStatus
    {
        public FileStatus(
            WorkingState working,
            StageState stage,
            LockState @lock,
            ConflictState conflict,
            RemoteState remote)
        {
            Working = working;
            Stage = stage;
            Lock = @lock;
            Conflict = conflict;
            Remote = remote;
        }

        public WorkingState Working { get; }

        public StageState Stage { get; }

        public LockState Lock { get; }

        public ConflictState Conflict { get; }

        public RemoteState Remote { get; }
    }
}
