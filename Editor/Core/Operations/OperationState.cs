namespace Lore.Unity.Core.Operations
{
    public enum OperationState
    {
        Preparing = 0,
        Executing,
        WaitingForUnity,
        Validating,
        Finalizing,
        Completed,
        Failed,
        Cancelled
    }
}
