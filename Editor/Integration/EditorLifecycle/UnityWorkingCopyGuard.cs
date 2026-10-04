using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Lore.Unity.Integration.EditorLifecycle
{
    // Construct on the Editor main thread. Never saves or discards user edits.
    // Deliberately not registered with the runtime until import/reload recovery is verified.
    public sealed class UnityWorkingCopyGuard : IWorkingCopyGuard
    {
        private readonly int _mainThread;
        private readonly SynchronizationContext _editorContext;
        private SceneSetup[] _sceneSetup;

        public UnityWorkingCopyGuard()
        {
            _editorContext = SynchronizationContext.Current;
            if (_editorContext == null ||
                _editorContext.GetType().FullName != "UnityEngine.UnitySynchronizationContext")
                throw new InvalidOperationException("Construct the guard on the Unity Editor main thread.");
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        public Task<Result> ValidateBeforeWriteAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            token.ThrowIfCancellationRequested();
            var state = CheckState();
            if (state.IsFailure) return Task.FromResult(state);
            if (PrefabStageUtility.GetCurrentPrefabStage() != null)
                return Task.FromResult(Fail("Close Prefab Mode before writing to the working copy."));
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
                    return Task.FromResult(Fail("Save or close untitled scenes before writing to the working copy."));
            _sceneSetup = EditorSceneManager.GetSceneManagerSetup();
            return Task.FromResult(Result.Success());
        }

        public async Task<Result> ValidateAfterWriteAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            token.ThrowIfCancellationRequested();
            var beforeRefresh = CheckState();
            if (beforeRefresh.IsFailure) return beforeRefresh;
            if (_sceneSetup == null || PrefabStageUtility.GetCurrentPrefabStage() != null ||
                !SameSetup(_sceneSetup, EditorSceneManager.GetSceneManagerSetup()))
                return Fail("Open scenes or Prefab Mode changed while Lore was writing.");
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                var afterImport = CheckState();
                if (afterImport.IsFailure) return afterImport;
                if (!SameSetup(_sceneSetup, EditorSceneManager.GetSceneManagerSetup()))
                    return Fail("Unity scene setup changed during import.");
                // Only reload clean, named scenes. This must not replace edits
                // created while Lore was running; CheckState just verified that.
                EditorSceneManager.RestoreSceneManagerSetup(_sceneSetup);
                var reloaded = CheckState();
                if (reloaded.IsFailure || !SameSetup(_sceneSetup, EditorSceneManager.GetSceneManagerSetup()))
                    return Fail("Unity scene reload could not be validated.");
                var stable = await WaitForStableEditorAsync(token);
                if (stable.IsFailure || !SameSetup(_sceneSetup, EditorSceneManager.GetSceneManagerSetup()))
                    return Fail("Unity compilation, import, or scene state did not stabilize.");
                _sceneSetup = null;
                return Result.Success();
            }
            catch (Exception e)
            {
                return Fail("Unity import could not be completed: " + e.GetType().Name);
            }
        }

        private Task<Result> WaitForStableEditorAsync(CancellationToken token)
        {
            var completion = new TaskCompletionSource<Result>();
            var started = EditorApplication.timeSinceStartup;
            var stableFrames = 0;
            void OnUpdate()
            {
                if (token.IsCancellationRequested || EditorApplication.timeSinceStartup - started > 30)
                {
                    EditorApplication.update -= OnUpdate;
                    completion.TrySetResult(Fail("Unity import stabilization timed out or was cancelled."));
                    return;
                }
                if (EditorApplication.isCompiling || EditorApplication.isUpdating)
                {
                    stableFrames = 0;
                    return;
                }
                if (++stableFrames < 2) return;
                EditorApplication.update -= OnUpdate;
                completion.TrySetResult(CheckState());
            }
            EditorApplication.update += OnUpdate;
            return completion.Task;
        }

        private static bool SameSetup(SceneSetup[] expected, SceneSetup[] current)
        {
            if (expected == null || current == null || expected.Length != current.Length) return false;
            for (var i = 0; i < expected.Length; i++)
                if (expected[i].path != current[i].path || expected[i].isLoaded != current[i].isLoaded ||
                    expected[i].isActive != current[i].isActive) return false;
            return true;
        }

        private Result CheckState()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread ||
                !ReferenceEquals(SynchronizationContext.Current, _editorContext))
                return Fail("Unity working copy check must run on the Editor main thread.");
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                return Fail("Unity is compiling, importing, or entering Play mode.");
            for (var i = 0; i < SceneManager.sceneCount; i++)
                if (SceneManager.GetSceneAt(i).isDirty)
                    return Fail("A scene has unsaved changes. Save it before writing to the working copy.");
            var prefab = PrefabStageUtility.GetCurrentPrefabStage();
            if (prefab != null && prefab.scene.isDirty)
                return Fail("A prefab stage has unsaved changes. Save it before writing to the working copy.");
            foreach (var loaded in Resources.FindObjectsOfTypeAll<UnityEngine.Object>())
                if (loaded != null && EditorUtility.IsDirty(loaded))
                    return Fail("A loaded Unity object has unsaved changes. Save it before writing to the working copy.");
            return Result.Success();
        }

        private static Result Fail(string message) =>
            Result.Failure(new LoreError(ErrorCode.ValidationFailed, message));
    }
}
