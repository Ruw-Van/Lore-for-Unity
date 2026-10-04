using System;
using System.Threading;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using UnityEditor;

namespace Lore.Unity.Integration.Assets
{
    public sealed class UnityGuidResolver : IUnityGuidLookup
    {
        private readonly int _mainThread;
        private readonly SynchronizationContext _context;

        public UnityGuidResolver()
        {
            _context = SynchronizationContext.Current;
            if (_context == null || _context.GetType().FullName != "UnityEngine.UnitySynchronizationContext")
                throw new InvalidOperationException("Construct the GUID resolver on the Unity Editor main thread.");
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        public string AssetPathToGuid(UnityAssetPath path)
        {
            EnsureMainThread();
            return AssetDatabase.AssetPathToGUID(path.Value);
        }

        public string GuidToAssetPath(string guid)
        {
            EnsureMainThread();
            return AssetDatabase.GUIDToAssetPath(guid);
        }

        private void EnsureMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread ||
                !ReferenceEquals(SynchronizationContext.Current, _context))
                throw new InvalidOperationException("AssetDatabase must be used on the Unity Editor main thread.");
        }
    }

    public sealed class UnityAssetSelectionResolver
    {
        private readonly IUnityGuidLookup _guids;
        private readonly UnityAssetPathMapper _paths;

        public UnityAssetSelectionResolver(IUnityGuidLookup guids, UnityAssetPathMapper paths)
        {
            _guids = guids ?? throw new ArgumentNullException(nameof(guids));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        }

        public Result<RepositoryPath> ResolveGuid(string guid)
        {
            if (guid == null || guid.Length != 32) return Invalid();
            foreach (var c in guid) if (!Uri.IsHexDigit(c)) return Invalid();
            var path = _guids.GuidToAssetPath(guid);
            if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                path.EndsWith(".meta", StringComparison.Ordinal) || path.IndexOf('\\') >= 0)
                return Invalid();
            try { return _paths.ToRepositoryPath(new UnityAssetPath(path)); }
            catch (ArgumentException) { return Invalid(); }
        }

        private static Result<RepositoryPath> Invalid() => Result<RepositoryPath>.Failure(
            new LoreError(ErrorCode.ValidationFailed, "GUID does not resolve to a project Asset."));
    }
}
