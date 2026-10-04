using Lore.Unity.Application.Runtime;
using Lore.Unity.Integration.Assets;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Errors;
using Lore.Unity.Infrastructure.Runtime;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Lore.Unity.Integration.EditorLifecycle
{
    // A validated manifest and installed runtime are required before backend activation.
    [InitializeOnLoad]
    public static class LoreBootstrap
    {
        private const string ManifestAssetPath =
            "Packages/com.ruwvan.lore-for-unity/Editor/Infrastructure/Runtime/runtime-manifest.json";
        private static RuntimeComposition _composition;
        private static UnityStatusProjection _projection;
        private static UnityChangeBridge _changes;
        private static RepositoryId _repository;
        private static LoreError _initialStatusError;
        private static readonly CancellationTokenSource Reload = new CancellationTokenSource();

        static LoreBootstrap()
        {
            _composition = RuntimeComposition.Create(null, RuntimeComposition.CurrentPlatform());
            EditorApplication.delayCall -= OnEditorReady;
            EditorApplication.delayCall += OnEditorReady;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        public static RuntimeContext Context => _composition.Context;
        public static UnityLogicalAssetIndex Assets => _projection?.Current;
        public static RepositoryId Repository => _repository;
        public static LoreError StatusError => _projection?.Current != null ? _changes?.LastError :
            _changes?.LastError ?? _initialStatusError;

        private static async void OnEditorReady()
        {
            EditorApplication.delayCall -= OnEditorReady;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(ManifestAssetPath);
            RuntimeManifest manifest = null;
            if (asset != null)
            {
                try { manifest = JsonUtility.FromJson<RuntimeManifest>(asset.text); }
                catch (System.ArgumentException) { /* Invalid manifest leaves Setup Required. */ }
            }
            _composition = RuntimeComposition.Create(manifest, RuntimeComposition.CurrentPlatform());
            if (_composition.Manager == null) return;
            try
            {
                var layout = RuntimeLayout.ForCurrentUser(RuntimeComposition.CurrentPlatform());
                var activated = await _composition.ActivateAsync(layout, new CliRuntimeProbe(), Reload.Token);
                if (Reload.IsCancellationRequested) return;
                _composition = activated;
                if (activated.Reads == null) return;
                var detector = activated.Reads.CreateDetector();
                var projectRoot = new AbsolutePath(
                    System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath));
                var repository = await detector.DetectAsync(projectRoot, Reload.Token);
                if (repository.IsSuccess && !Reload.IsCancellationRequested)
                {
                    var mapper = new UnityAssetPathMapper(projectRoot, repository.Value.Root);
                    var projection = new UnityStatusProjection(activated.Reads.Status, activated.Reads.Store,
                        mapper, new UnityGuidResolver());
                    _repository = repository.Value.Id;
                    _projection = projection;
                    _changes = new UnityChangeBridge(projection, _repository, Reload.Token);
                    var initial = await projection.RefreshAsync(repository.Value.Id, Reload.Token);
                    if (initial.IsFailure && !Reload.IsCancellationRequested)
                    {
                        _initialStatusError = initial.Error;
                        UnityChangeBridge.Hint("Assets/"); // retry on next Editor update
                    }
                }
            }
            catch (System.OperationCanceledException) { /* Domain reload. */ }
            catch (System.IO.IOException) { /* Cache unavailable; remain Setup Required. */ }
            catch (System.UnauthorizedAccessException) { /* Cache unavailable. */ }
        }

        private static void OnBeforeReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            _changes?.Dispose();
            _changes = null;
            _projection = null;
            _repository = null;
            _initialStatusError = null;
            Reload.Cancel();
            EditorApplication.delayCall -= OnEditorReady;
            _composition = null;
        }
    }
}
