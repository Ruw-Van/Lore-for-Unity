namespace Lore.Unity.Core.Errors
{
    public enum ErrorCode
    {
        None = 0,
        NetworkUnavailable,
        AuthenticationRequired,
        Conflict,
        Locked,
        InvalidRepository,
        UnsupportedOperation,
        RuntimeMissing,
        RuntimeCorrupted,
        VersionMismatch,
        ValidationFailed,
        Unknown
    }
}
