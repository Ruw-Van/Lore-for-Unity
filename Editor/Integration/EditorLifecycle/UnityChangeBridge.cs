using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Integration.Assets;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Lore.Unity.Integration.EditorLifecycle
{
    // Unity callbacks only enqueue hints. Lore remains the source of truth.
    public sealed class UnityChangeBridge : IDisposable
    {
        private static UnityChangeBridge _active;
        private readonly UnityStatusProjection _projection;
        private readonly RepositoryId _repository;
        private readonly CancellationToken _reload;
        private readonly HashSet<string> _hints = new HashSet<string>(StringComparer.Ordinal);
        private double _lastHint;
        private bool _running;
        private bool _disposed;

        public UnityChangeBridge(UnityStatusProjection projection, RepositoryId repository, CancellationToken reload)
        {
            _projection = projection ?? throw new ArgumentNullException(nameof(projection));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _reload = reload;
            if (_active != null) throw new InvalidOperationException("Unity change bridge already exists.");
            _active = this;
            EditorApplication.update -= OnUpdate;
            EditorApplication.update += OnUpdate;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            EditorSceneManager.sceneSaved += OnSceneSaved;
        }

        public LoreError LastError { get; private set; }

        internal static void Hint(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !(path.StartsWith("Assets/", StringComparison.Ordinal) ||
                  path.StartsWith("ProjectSettings/", StringComparison.Ordinal) ||
                  path.StartsWith("Packages/", StringComparison.Ordinal))) return;
            var bridge = _active;
            if (bridge == null || bridge._disposed) return;
            bridge._hints.Add(path);
            bridge._lastHint = EditorApplication.timeSinceStartup;
        }

        private static void OnSceneSaved(Scene scene) => Hint(scene.path);

        private static void OnUpdate()
        {
            var bridge = _active;
            if (bridge == null || bridge._disposed || bridge._running || bridge._hints.Count == 0 ||
                bridge._reload.IsCancellationRequested || EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.timeSinceStartup - bridge._lastHint < 0.25) return;
            bridge._hints.Clear();
            bridge._running = true;
            _ = bridge.RefreshAsync();
        }

        private async Task RefreshAsync()
        {
            try
            {
                var result = await _projection.RefreshAsync(_repository, _reload);
                if (!_disposed) LastError = result.IsFailure ? result.Error : null;
            }
            catch (OperationCanceledException) { /* Assembly reload. */ }
            catch (Exception error)
            {
                if (!_disposed) LastError = new LoreError(ErrorCode.Unknown,
                    "Unity status refresh failed: " + error.GetType().Name);
            }
            finally { _running = false; }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            EditorApplication.update -= OnUpdate;
            EditorSceneManager.sceneSaved -= OnSceneSaved;
            if (ReferenceEquals(_active, this)) _active = null;
            _hints.Clear();
        }
    }

    public sealed class LoreAssetPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted,
            string[] moved, string[] movedFrom)
        {
            foreach (var path in imported) UnityChangeBridge.Hint(path);
            foreach (var path in deleted) UnityChangeBridge.Hint(path);
            foreach (var path in moved) UnityChangeBridge.Hint(path);
            foreach (var path in movedFrom) UnityChangeBridge.Hint(path);
        }
    }
}
