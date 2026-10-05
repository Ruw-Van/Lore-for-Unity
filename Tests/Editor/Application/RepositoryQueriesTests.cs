using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Queries;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class RepositoryQueriesTests
    {
        private sealed class Backend : IRepositoryQueryBackend
        {
            public int LastLimit;
            public Task<Result<IReadOnlyList<BranchName>>> ListBranchesAsync(RepositoryId repository,
                CancellationToken token)
            {
                IReadOnlyList<BranchName> items = new[] { new BranchName("main") };
                return Task.FromResult(Result<IReadOnlyList<BranchName>>.Success(items));
            }
            public Task<Result<IReadOnlyList<RevisionHistoryEntry>>> HistoryAsync(RepositoryId repository,
                int limit, CancellationToken token)
            {
                LastLimit = limit;
                IReadOnlyList<RevisionHistoryEntry> items = Array.Empty<RevisionHistoryEntry>();
                return Task.FromResult(Result<IReadOnlyList<RevisionHistoryEntry>>.Success(items));
            }
        }

        [Test]
        public void HistoryLimitIsBoundedBeforeBackendCall()
        {
            var backend = new Backend();
            var service = new RepositoryQueries(backend);
            Assert.Throws<ArgumentOutOfRangeException>(() => service.HistoryAsync(new RepositoryId("repo"), 0,
                CancellationToken.None));
            Assert.Throws<ArgumentOutOfRangeException>(() => service.HistoryAsync(new RepositoryId("repo"), 201,
                CancellationToken.None));
            Assert.That(service.HistoryAsync(new RepositoryId("repo"), 50, CancellationToken.None).Result.IsSuccess, Is.True);
            Assert.That(backend.LastLimit, Is.EqualTo(50));
        }
    }
}
