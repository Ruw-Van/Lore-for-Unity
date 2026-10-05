using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Queries
{
    public sealed class RevisionHistoryEntry
    {
        public RevisionHistoryEntry(RevisionSignature revision, RevisionNumber number,
            string message, DateTime timestampUtc)
        {
            Revision = revision ?? throw new ArgumentNullException(nameof(revision));
            if (message == null) throw new ArgumentNullException(nameof(message));
            if (timestampUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(timestampUtc));
            Number = number;
            Message = message;
            TimestampUtc = timestampUtc;
        }
        public RevisionSignature Revision { get; }
        public RevisionNumber Number { get; }
        public string Message { get; }
        public DateTime TimestampUtc { get; }
    }

    public interface IRepositoryQueryBackend
    {
        Task<Result<IReadOnlyList<BranchName>>> ListBranchesAsync(RepositoryId repository, CancellationToken token);
        Task<Result<IReadOnlyList<RevisionHistoryEntry>>> HistoryAsync(RepositoryId repository,
            int limit, CancellationToken token);
    }

    public sealed class RepositoryQueries
    {
        private readonly IRepositoryQueryBackend _backend;
        public RepositoryQueries(IRepositoryQueryBackend backend)
        { _backend = backend ?? throw new ArgumentNullException(nameof(backend)); }

        public Task<Result<IReadOnlyList<BranchName>>> ListBranchesAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            return _backend.ListBranchesAsync(repository, token);
        }

        public Task<Result<IReadOnlyList<RevisionHistoryEntry>>> HistoryAsync(RepositoryId repository,
            int limit, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (limit < 1 || limit > 200) throw new ArgumentOutOfRangeException(nameof(limit));
            return _backend.HistoryAsync(repository, limit, token);
        }
    }
}
