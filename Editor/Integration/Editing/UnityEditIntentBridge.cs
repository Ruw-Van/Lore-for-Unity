using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Lore.Unity.Integration.Editing
{
    public sealed class UnityEditIntentBridge : IDisposable
    {
        private readonly UnityLockIntentController _controller;
        private readonly CancellationToken _reload;
        private readonly HashSet<UnityAssetPath> _pending = new HashSet<UnityAssetPath>();
        private bool _disposed;
        private double _lastHint;

        public UnityEditIntentBridge(UnityLockIntentController controller, CancellationToken reload)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _reload = reload;
            Undo.postprocessModifications -= OnModifications;
            Undo.postprocessModifications += OnModifications;
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
        }

        public LoreError LastError { get; private set; }

        private UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
        {
            if (_disposed || _reload.IsCancellationRequested) return modifications;
            if (modifications == null) return null;
            foreach (var modification in modifications)
            {
                var target = modification.currentValue.target;
                var path = PathForTarget(target);
                if (string.IsNullOrEmpty(path) || path.IndexOf('\\') >= 0 ||
                    !(path.StartsWith("Assets/", StringComparison.Ordinal) ||
                      path.StartsWith("ProjectSettings/", StringComparison.Ordinal) ||
                      path.StartsWith("Packages/", StringComparison.Ordinal))) continue;
                _pending.Add(new UnityAssetPath(path));
                _lastHint = EditorApplication.timeSinceStartup;
            }
            return modifications; // Never alter or block Unity's edit callback.
        }

        private static string PathForTarget(UnityEngine.Object target)
        {
            GameObject gameObject = target is Component component ? component.gameObject : target as GameObject;
            if (gameObject != null)
            {
                var stage = PrefabStageUtility.GetCurrentPrefabStage();
                if (stage != null && gameObject.scene.Equals(stage.scene)) return stage.assetPath;
                return gameObject.scene.path;
            }
            return target == null ? null : AssetDatabase.GetAssetPath(target);
        }

        private void OnUpdate()
        {
            if (_disposed || _reload.IsCancellationRequested || _pending.Count == 0 ||
                EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup - _lastHint < 0.15) return;
            var paths = new List<UnityAssetPath>(_pending);
            _pending.Clear();
            var text = EditorSettings.serializationMode == SerializationMode.ForceText;
            foreach (var path in paths)
            {
                try { _ = ObserveAsync(_controller.RequestAsync(path, text, _reload)); }
                catch (Exception error)
                {
                    LastError = new LoreError(ErrorCode.ValidationFailed,
                        "Lock intent could not be prepared: " + error.GetType().Name);
                }
            }
        }

        private async Task ObserveAsync(Task<UnityLockIntentResult> request)
        {
            try
            {
                var result = await request;
                if (!_disposed && result.Outcome != null)
                    LastError = result.Outcome.Result.IsFailure ? result.Outcome.Result.Error : null;
            }
            catch (OperationCanceledException) { /* Assembly reload. */ }
            catch (Exception error)
            {
                if (!_disposed) LastError = new LoreError(ErrorCode.Unknown,
                    "Lock intent failed: " + error.GetType().Name);
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Undo.postprocessModifications -= OnModifications;
            EditorApplication.update -= OnUpdate;
            _pending.Clear();
        }
    }
}
