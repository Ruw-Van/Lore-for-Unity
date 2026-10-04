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

        public UnityWorkingCopyGuard()
        {
            _mainThread = Thread.CurrentThread.ManagedThreadId;
        }

        public Task<Result> ValidateBeforeWriteAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            token.ThrowIfCancellationRequested();
            return Task.FromResult(CheckState());
        }

        public Task<Result> ValidateAfterWriteAsync(RepositoryId repository, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            token.ThrowIfCancellationRequested();
            var beforeRefresh = CheckState();
            if (beforeRefresh.IsFailure) return Task.FromResult(beforeRefresh);
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                return Task.FromResult(CheckState());
            }
            catch (Exception e)
            {
                return Task.FromResult(Fail("Unity import could not be completed: " + e.GetType().Name));
            }
        }

        private Result CheckState()
        {
            if (Thread.CurrentThread.ManagedThreadId != _mainThread)
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
