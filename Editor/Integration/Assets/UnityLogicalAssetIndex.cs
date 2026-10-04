using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.Status;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Integration.Assets
{
    public interface IUnityGuidLookup
    {
        // Main-thread only. Empty means the asset cannot be resolved in the
        // current AssetDatabase (e.g. it was deleted); never fabricate a GUID.
        string AssetPathToGuid(UnityAssetPath path);
        string GuidToAssetPath(string guid);
    }

    public sealed class UnityLogicalAsset
    {
        public UnityLogicalAsset(UnityAssetPath path, string guid,
            FileStatusEntry asset, FileStatusEntry meta)
        {
            Path = path;
            Guid = guid;
            Asset = asset;
            Meta = meta;
        }
        public UnityAssetPath Path { get; }
        public string Guid { get; }
        public FileStatusEntry Asset { get; }
        public FileStatusEntry Meta { get; }
        public bool HasOnlyMetaChange => Asset == null && Meta != null;
    }

    public sealed class UnityLogicalAssetIndex
    {
        private readonly Dictionary<RepositoryPath, UnityLogicalAsset> _byRepositoryPath;
        private readonly Dictionary<UnityAssetPath, UnityLogicalAsset> _byAssetPath;
        private readonly Dictionary<string, UnityLogicalAsset> _byGuid;
        private readonly Dictionary<UnityAssetPath, int> _folderCounts;

        private UnityLogicalAssetIndex(List<UnityLogicalAsset> assets, List<FileStatusEntry> files,
            Dictionary<RepositoryPath, UnityLogicalAsset> repositoryPaths,
            Dictionary<UnityAssetPath, UnityLogicalAsset> assetPaths,
            Dictionary<string, UnityLogicalAsset> guids, Dictionary<UnityAssetPath, int> folderCounts)
        {
            Assets = new ReadOnlyCollection<UnityLogicalAsset>(assets);
            RepositoryFiles = new ReadOnlyCollection<FileStatusEntry>(files);
            _byRepositoryPath = repositoryPaths;
            _byAssetPath = assetPaths;
            _byGuid = guids;
            _folderCounts = folderCounts;
        }

        public IReadOnlyList<UnityLogicalAsset> Assets { get; }
        public IReadOnlyList<FileStatusEntry> RepositoryFiles { get; }
        public bool TryGet(RepositoryPath path, out UnityLogicalAsset asset) =>
            _byRepositoryPath.TryGetValue(path, out asset);
        public bool TryGet(UnityAssetPath path, out UnityLogicalAsset asset) =>
            _byAssetPath.TryGetValue(path, out asset);
        public bool TryGetGuid(string guid, out UnityLogicalAsset asset) =>
            _byGuid.TryGetValue(guid ?? string.Empty, out asset);
        public int ChangedWithinFolder(UnityAssetPath folder) =>
            _folderCounts.TryGetValue(folder, out var count) ? count : 0;

        public static Result<UnityLogicalAssetIndex> Build(StatusSnapshot snapshot,
            UnityAssetPathMapper paths, IUnityGuidLookup guids)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (guids == null) throw new ArgumentNullException(nameof(guids));
            var grouped = new Dictionary<UnityAssetPath, (FileStatusEntry asset, FileStatusEntry meta)>();
            var files = new List<FileStatusEntry>();
            foreach (var entry in snapshot.Entries)
            {
                var converted = paths.ToUnityPath(entry.Path);
                if (converted.IsFailure)
                {
                    files.Add(entry); // Outside the Unity project or not an AssetDatabase path.
                    continue;
                }
                var value = converted.Value.Value;
                if (!value.StartsWith("Assets/", StringComparison.Ordinal))
                {
                    files.Add(entry); // ProjectSettings and Packages are repository files.
                    continue;
                }
                var isMeta = value.EndsWith(".meta", StringComparison.Ordinal);
                var assetPath = isMeta ? value.Substring(0, value.Length - ".meta".Length) : value;
                if (assetPath == "Assets/" || assetPath.Length == 0) return Invalid();
                var key = new UnityAssetPath(assetPath);
                grouped.TryGetValue(key, out var pair);
                if (isMeta)
                {
                    if (pair.meta != null) return Invalid();
                    pair.meta = entry;
                }
                else
                {
                    if (pair.asset != null) return Invalid();
                    pair.asset = entry;
                }
                grouped[key] = pair;
            }

            var assets = new List<UnityLogicalAsset>();
            var byRepository = new Dictionary<RepositoryPath, UnityLogicalAsset>();
            var byAsset = new Dictionary<UnityAssetPath, UnityLogicalAsset>();
            var byGuid = new Dictionary<string, UnityLogicalAsset>(StringComparer.OrdinalIgnoreCase);
            var folders = new Dictionary<UnityAssetPath, int>();
            foreach (var pair in grouped)
            {
                var guid = guids.AssetPathToGuid(pair.Key);
                if (!string.IsNullOrEmpty(guid))
                {
                    if (guid.Length != 32) return Invalid();
                    foreach (var c in guid) if (!Uri.IsHexDigit(c)) return Invalid();
                }
                var asset = new UnityLogicalAsset(pair.Key, string.IsNullOrEmpty(guid) ? null : guid,
                    pair.Value.asset, pair.Value.meta);
                if (asset.Guid != null && !byGuid.TryAdd(asset.Guid, asset)) return Invalid();
                assets.Add(asset);
                byAsset.Add(pair.Key, asset);
                if (asset.Asset != null) byRepository.Add(asset.Asset.Path, asset);
                if (asset.Meta != null) byRepository.Add(asset.Meta.Path, asset);
                var relative = pair.Key.Value;
                for (var slash = relative.IndexOf('/'); slash >= 0; slash = relative.IndexOf('/', slash + 1))
                {
                    var folder = new UnityAssetPath(relative.Substring(0, slash));
                    folders.TryGetValue(folder, out var count);
                    folders[folder] = count + 1;
                }
            }
            return Result<UnityLogicalAssetIndex>.Success(new UnityLogicalAssetIndex(assets, files,
                byRepository, byAsset, byGuid, folders));
        }

        private static Result<UnityLogicalAssetIndex> Invalid() =>
            Result<UnityLogicalAssetIndex>.Failure(new LoreError(ErrorCode.ValidationFailed,
                "Unity asset status has ambiguous paths or duplicate GUIDs."));
    }
}
