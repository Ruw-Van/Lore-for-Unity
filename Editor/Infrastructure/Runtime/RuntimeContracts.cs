using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.Runtime
{
    public sealed class RuntimeProjectRecord
    {
        public RuntimeProjectRecord(ProjectId projectId, AbsolutePath projectPath, LoreVersion requiredVersion, DateTime lastSeenUtc)
        {
            ProjectId = projectId ?? throw new ArgumentNullException(nameof(projectId));
            if (string.IsNullOrEmpty(projectPath.Value)) throw new ArgumentException("Project path is required.", nameof(projectPath));
            if (lastSeenUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC is required.", nameof(lastSeenUtc));
            ProjectPath = projectPath;
            RequiredLoreVersion = requiredVersion ?? throw new ArgumentNullException(nameof(requiredVersion));
            LastSeenUtc = lastSeenUtc;
        }
        public ProjectId ProjectId { get; }
        public AbsolutePath ProjectPath { get; }
        public LoreVersion RequiredLoreVersion { get; }
        public DateTime LastSeenUtc { get; }
    }

    public interface IRuntimeRegistry
    {
        // Implementations must replace the persistent registry atomically under a cross-process lock.
        IReadOnlyList<RuntimeProjectRecord> Read();
        void Record(RuntimeProjectRecord project);
    }

    public interface IRuntimeUsageRegistry
    {
        // A stale registry entry alone is not evidence that an Editor or Lore process is running.
        bool IsInUse(LoreVersion version, string platform);
    }

    public interface IRuntimeLockManager
    {
        // Null means another process holds the lock. Caller owns the returned lease.
        IDisposable TryAcquire();
    }

    public interface IRuntimeProbe
    {
        Task<Result> VerifyInstalledAsync(AbsolutePath installation, LoreVersion version,
            string platform, CancellationToken cancellationToken);
    }

    public interface IRuntimeInstaller
    {
        // Must validate archive entries, extract to temporary storage, probe expected files/version,
        // and atomically publish only after validation. No unverified implementation is registered.
        Task<Result> InstallAsync(AbsolutePath verifiedArtifact, AbsolutePath destination,
            ValidatedRuntimeArtifact artifact, LoreVersion version, CancellationToken cancellationToken);
    }

    public interface IRuntimeDownloader
    {
        // Invoked only by an explicit user Install action; never during Editor bootstrap.
        Task<Result<AbsolutePath>> DownloadAsync(ValidatedRuntimeArtifact artifact, CancellationToken cancellationToken);
    }
}
