using Lore.Unity.Application.Runtime;
using Lore.Unity.Infrastructure.Runtime;
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

        static LoreBootstrap()
        {
            _composition = RuntimeComposition.Create(null, RuntimeComposition.CurrentPlatform());
            EditorApplication.delayCall -= OnEditorReady;
            EditorApplication.delayCall += OnEditorReady;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        public static RuntimeContext Context => _composition.Context;

        private static void OnEditorReady()
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
        }

        private static void OnBeforeReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            EditorApplication.delayCall -= OnEditorReady;
            _composition = null;
        }
    }
}
