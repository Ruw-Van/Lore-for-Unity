using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Status;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Integration.Assets
{
    public sealed class UnityStatusProjection
    {
        private readonly StatusReader _reader;
        private readonly StatusStore _store;
        private readonly UnityAssetPathMapper _paths;
        private readonly IUnityGuidLookup _guids;
        private readonly int _mainThread;
        private StatusSnapshot _source;
        private UnityLogicalAssetIndex _index;

        public UnityStatusProjection(StatusReader reader, StatusStore store,
            UnityAssetPathMapper paths, IUnityGuidLookup guids)
        {
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _guids = guids ?? throw new ArgumentNullException(nameof(guids));
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        // A newer Lore Status invalidates this derived snapshot until it is rebuilt.
        public UnityLogicalAssetIndex Current => ReferenceEquals(_source, _store.Current) ? _index : null;

        public async Task<Result<UnityLogicalAssetIndex>> RefreshAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (Thread.CurrentThread.ManagedThreadId != _mainThread) return OffThread();
            var refreshed = await _reader.RefreshRepositoryAsync(repository, token);
            token.ThrowIfCancellationRequested();
            if (Thread.CurrentThread.ManagedThreadId != _mainThread) return OffThread();
            if (refreshed.IsFailure) return Result<UnityLogicalAssetIndex>.Failure(refreshed.Error);
            var built = UnityLogicalAssetIndex.Build(refreshed.Value, _paths, _guids);
            if (built.IsFailure) return built;
            if (!ReferenceEquals(refreshed.Value, _store.Current))
                return Result<UnityLogicalAssetIndex>.Failure(new LoreError(ErrorCode.ValidationFailed,
                    "Stale Unity status projection was discarded."));
            _source = refreshed.Value;
            _index = built.Value;
            return built;
        }

        private static Result<UnityLogicalAssetIndex> OffThread() =>
            Result<UnityLogicalAssetIndex>.Failure(new LoreError(ErrorCode.ValidationFailed,
                "Unity status projection must run on the Editor main thread."));
    }
}
