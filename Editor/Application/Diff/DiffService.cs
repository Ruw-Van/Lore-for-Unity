using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Diff
{
    public enum DiffMode { Simple = 0, Structured = 1 }

    public interface IStructuredDiffBackend
    {
        Task<Result<string>> ReadAsync(RepositoryId repository, RepositoryPath path, CancellationToken token);
    }

    public sealed class DiffService
    {
        private readonly IDiffBackend _simple;
        private readonly IStructuredDiffBackend _structured;

        public DiffService(IDiffBackend simple, IStructuredDiffBackend structured = null)
        {
            _simple = simple ?? throw new ArgumentNullException(nameof(simple));
            _structured = structured;
        }

        public Task<Result<string>> ReadAsync(RepositoryId repository, RepositoryPath path,
            DiffMode mode, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (string.IsNullOrEmpty(path.Value)) throw new ArgumentException("Path required.", nameof(path));
            if (mode == DiffMode.Simple) return _simple.ReadTextAsync(repository, path, token);
            if (mode == DiffMode.Structured && _structured != null)
                return _structured.ReadAsync(repository, path, token);
            return Task.FromResult(Result<string>.Failure(new LoreError(ErrorCode.UnsupportedOperation,
                "Structured Diff is not available for this file.")));
        }
    }
}
