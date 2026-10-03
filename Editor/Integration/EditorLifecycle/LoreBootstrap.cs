using Lore.Unity.Application.Runtime;
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
                var projectRoot = new Lore.Unity.Core.Paths.AbsolutePath(
                    System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath));
                var repository = await detector.DetectAsync(projectRoot, Reload.Token);
                if (repository.IsSuccess && !Reload.IsCancellationRequested)
                    await activated.Reads.Status.RefreshRepositoryAsync(repository.Value.Id, Reload.Token);
            }
            catch (System.OperationCanceledException) { /* Domain reload. */ }
            catch (System.IO.IOException) { /* Cache unavailable; remain Setup Required. */ }
            catch (System.UnauthorizedAccessException) { /* Cache unavailable. */ }
        }

        private static void OnBeforeReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            Reload.Cancel();
            EditorApplication.delayCall -= OnEditorReady;
            _composition = null;
        }
    }
}
