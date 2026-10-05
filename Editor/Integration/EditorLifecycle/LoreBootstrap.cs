using Lore.Unity.Application.Runtime;
using Lore.Unity.Integration.Assets;
using Lore.Unity.Integration.Editing;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Errors;
using Lore.Unity.Application.Queries;
using Lore.Unity.Application.Diff;
using Lore.Unity.Application.Conflicts;
using Lore.Unity.Application.Status;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Repository;
using Lore.Unity.Infrastructure.Recovery;
using Lore.Unity.Infrastructure.Runtime;
using Lore.Unity.Infrastructure.Repository;
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
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
        private static UnityEditIntentBridge _editing;
        private static UnityAssetPathMapper _paths;
        private static WriteBackendComposition _writes;
        private static Task<Result<WriteBackendComposition>> _enabling;
        private static Task<Result> _installing;
        private static Task<Result> _initializing;
        private static bool _detecting;
        private static RepositoryId _repository;
        private static RepositorySnapshot _detected;
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
        public static RepositorySnapshot DetectedRepository => _detected;
        public static WriteBackendComposition Writes => _writes;
        public static LoreError LockError => _editing?.LastError;
        public static StatusSnapshot Status => _composition?.Reads?.Store.Current;
        public static RepositoryQueries Queries => _composition?.Reads?.Queries;
        public static DiffService Diff => _composition?.Reads?.Diff;
        public static string RequiredVersion => _composition?.RequiredVersion;
        public static bool CanInitializeRepository => (_initializing == null || _initializing.IsCompleted) &&
            CanInitializeRepositoryCore();

        private static bool CanInitializeRepositoryCore() => !_detecting && _repository == null &&
            _composition?.Context.Availability == RuntimeAvailability.Ready &&
            _composition.Reads?.CreateInitializer() != null &&
            LoreRepositoryInitializer.CanInitialize(ProjectRoot());

        private static AbsolutePath ProjectRoot() => new AbsolutePath(
            Path.GetDirectoryName(UnityEngine.Application.dataPath));
        public static Result<ValidatedRuntimeArtifact> RuntimeArtifact =>
            _composition?.Manager == null || RuntimeComposition.CurrentPlatform() == null
                ? Result<ValidatedRuntimeArtifact>.Failure(new LoreError(ErrorCode.UnsupportedOperation,
                    "A valid manifest and supported platform are required."))
                : _composition.Manager.FindArtifact(RuntimeComposition.CurrentPlatform());
        public static string AssetRootPrefix => _paths?.AssetRootPrefix;
        public static FileRecoveryJournal Recovery => _repository == null ? null : new FileRecoveryJournal(
            new AbsolutePath(Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath),
                "Library", "LoreForUnity", "Recovery")));
        public static Result<ConflictService> CreateConflictRecovery()
        {
            if (_repository == null || _composition?.Reads == null)
                return Result<ConflictService>.Failure(new LoreError(ErrorCode.InvalidRepository,
                    "No verified Lore repository is available."));
            return _composition.Reads.CreateConflictRecovery(new UnityWorkingCopyGuard(), Recovery);
        }
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
                await ActivateAndDetectAsync(layout, Reload.Token);
            }
            catch (System.OperationCanceledException) { /* Domain reload. */ }
            catch (System.IO.IOException) { /* Cache unavailable; remain Setup Required. */ }
            catch (System.UnauthorizedAccessException) { /* Cache unavailable. */ }
        }

        public static Task<Result> InstallOfficialAsync(CancellationToken token,
            IProgress<RuntimeInstallProgress> progress = null) =>
            StartInstallAsync((manager, platform, ct) => manager.InstallOfficialAsync(platform, ct, progress), token, progress);

        public static Task<Result> InstallFromFileAsync(AbsolutePath file, CancellationToken token,
            IProgress<RuntimeInstallProgress> progress = null) =>
            StartInstallAsync((manager, platform, ct) => manager.InstallFromFileAsync(file, platform, ct, progress),
                token, progress);

        public static Task<Result> InitializeRepositoryAsync(CancellationToken token)
        {
            if (_initializing != null && !_initializing.IsCompleted)
                return Task.FromResult(Result.Failure(new LoreError(ErrorCode.Locked,
                    "Repository initialization is already in progress.")));
            _initializing = InitializeRepositoryCoreAsync(token);
            return _initializing;
        }

        private static async Task<Result> InitializeRepositoryCoreAsync(CancellationToken token)
        {
            try
            {
                if (!CanInitializeRepositoryCore())
                    return Result.Failure(new LoreError(ErrorCode.InvalidRepository,
                        "This project is already managed or Lore Runtime is unavailable."));
                var root = ProjectRoot();
                var created = await _composition.Reads.CreateInitializer().InitializeAsync(root, token);
                if (created.IsFailure) return created;
                if (Reload.IsCancellationRequested)
                    return Result.Failure(new LoreError(ErrorCode.Cancelled,
                        "Lore repository was created, but Editor reload interrupted verification. Reopen the project."));
                var detected = await DetectCurrentProjectAsync(token);
                if (detected.IsFailure) return Result.Failure(new LoreError(ErrorCode.InvalidRepository,
                    "Lore repository was created, but could not be verified. Inspect it before retrying."));
                return Result.Success();
            }
            catch (OperationCanceledException)
            { return Result.Failure(new LoreError(ErrorCode.Cancelled,
                "Repository initialization was interrupted. Check for .lore before retrying.")); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            { return Result.Failure(new LoreError(ErrorCode.InvalidRepository,
                "Repository initialization failed. Check the project directory before retrying.")); }
        }

        private static Task<Result> StartInstallAsync(
            Func<LoreRuntimeManager, string, CancellationToken, Task<Result>> action, CancellationToken token,
            IProgress<RuntimeInstallProgress> progress)
        {
            if (_installing != null && !_installing.IsCompleted)
                return Task.FromResult(Result.Failure(new LoreError(ErrorCode.Locked,
                    "A runtime installation is already in progress.")));
            _installing = InstallCoreAsync(action, token, progress);
            return _installing;
        }

        private static async Task<Result> InstallCoreAsync(
            Func<LoreRuntimeManager, string, CancellationToken, Task<Result>> action, CancellationToken token,
            IProgress<RuntimeInstallProgress> progress)
        {
            try
            {
                var current = _composition;
                var platform = RuntimeComposition.CurrentPlatform();
                if (current?.Manager == null || platform == null)
                    return Result.Failure(new LoreError(ErrorCode.UnsupportedOperation,
                        "A supported platform and valid runtime manifest are required."));
                if (current.Context.Availability == RuntimeAvailability.Ready)
                    return Result.Failure(new LoreError(ErrorCode.ValidationFailed,
                        "Lore Runtime is already installed and verified."));
                var installed = await action(current.Manager, platform, token);
                if (installed.IsFailure) return installed;
                if (Reload.IsCancellationRequested)
                    return Result.Failure(new LoreError(ErrorCode.Cancelled,
                        "Assembly reload interrupted runtime installation."));
                progress?.Report(new RuntimeInstallProgress(RuntimeInstallStage.Activating));
                var activated = await ActivateAndDetectAsync(RuntimeLayout.ForCurrentUser(platform), token);
                return activated;
            }
            catch (OperationCanceledException)
            { return Result.Failure(new LoreError(ErrorCode.Cancelled, "Runtime installation was cancelled.")); }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidOperationException)
            { return Result.Failure(new LoreError(ErrorCode.RuntimeMissing, "Runtime installation could not be completed.")); }
            finally { _installing = null; }
        }

        private static async Task<Result> ActivateAndDetectAsync(RuntimeLayout layout, CancellationToken token)
        {
            var activated = await _composition.ActivateAsync(layout, new CliRuntimeProbe(), token);
            if (Reload.IsCancellationRequested)
                return Result.Failure(new LoreError(ErrorCode.Cancelled, "Assembly reload interrupted runtime activation."));
            _composition = activated;
            if (activated.Reads == null)
                return Result.Failure(new LoreError(ErrorCode.RuntimeCorrupted,
                    "Installed Lore Runtime could not be activated."));
            var detected = await DetectCurrentProjectAsync(token);
            // A valid Runtime does not require this Unity project to be a Lore repository.
            return detected.IsFailure && !Reload.IsCancellationRequested ? Result.Success() : detected;
        }

        private static async Task<Result> DetectCurrentProjectAsync(CancellationToken token)
        {
            _detecting = true;
            try
            {
                var detector = _composition.Reads.CreateDetector();
                var projectRoot = ProjectRoot();
                var repository = await detector.DetectAsync(projectRoot, token);
                if (repository.IsFailure) return Result.Failure(repository.Error);
                if (Reload.IsCancellationRequested)
                    return Result.Failure(new LoreError(ErrorCode.Cancelled, "Editor reload interrupted repository detection."));
                var mapper = new UnityAssetPathMapper(projectRoot, repository.Value.Root);
                var projection = new UnityStatusProjection(_composition.Reads.Status, _composition.Reads.Store,
                    mapper, new UnityGuidResolver());
                _repository = repository.Value.Id;
                _detected = repository.Value;
                _paths = mapper;
                _projection = projection;
                _changes = new UnityChangeBridge(projection, _repository, Reload.Token);
                var initial = await projection.RefreshAsync(repository.Value.Id, token);
                if (initial.IsFailure && !Reload.IsCancellationRequested)
                {
                    _initialStatusError = initial.Error;
                    UnityChangeBridge.Hint("Assets/");
                }
                return Result.Success();
            }
            finally { _detecting = false; }
        }

        // Explicit opt-in only. UI/controller code may call this after presenting
        // the working-copy and remote-lock implications to the user.
        public static Task<Result<WriteBackendComposition>> EnableEditingAsync(CancellationToken token)
        {
            if (_writes != null) return Task.FromResult(Result<WriteBackendComposition>.Success(_writes));
            if (_enabling != null && !_enabling.IsCompleted) return _enabling;
            _enabling = EnableCoreAsync(token);
            return _enabling;
        }

        private static async Task<Result<WriteBackendComposition>> EnableCoreAsync(CancellationToken token)
        {
            try
            {
                if (_repository == null || _paths == null || _composition == null)
                    return Result<WriteBackendComposition>.Failure(new LoreError(ErrorCode.InvalidRepository,
                        "A verified Lore repository is required before enabling edits."));
                var guard = new UnityWorkingCopyGuard();
                var projectRoot = Path.GetDirectoryName(UnityEngine.Application.dataPath);
                var journal = new FileRecoveryJournal(new AbsolutePath(Path.Combine(projectRoot,
                    "Library", "LoreForUnity", "Recovery")));
                var composition = _composition;
                var enabled = await Task.Run(() => composition.CreateWrites(guard, journal), token);
                token.ThrowIfCancellationRequested();
                if (Reload.IsCancellationRequested)
                    return Result<WriteBackendComposition>.Failure(new LoreError(ErrorCode.Cancelled,
                        "Assembly reload cancelled write integration."));
                if (enabled.IsFailure) return enabled;
                var controller = new UnityLockIntentController(new LoreLockRequester(enabled.Value.Lock),
                    _repository, _paths, () => UnityProjectLockSettings.instance.ReadPolicy());
                _editing = new UnityEditIntentBridge(controller, Reload.Token);
                _writes = enabled.Value;
                return enabled;
            }
            catch (OperationCanceledException)
            {
                return Result<WriteBackendComposition>.Failure(new LoreError(ErrorCode.Cancelled,
                    "Write integration was cancelled."));
            }
            catch (InvalidOperationException)
            {
                return Result<WriteBackendComposition>.Failure(new LoreError(ErrorCode.ValidationFailed,
                    "Unity editing integration must be enabled on the Editor main thread."));
            }
            finally { _enabling = null; }
        }

        public static Task<Result<UnityLogicalAssetIndex>> RefreshStatusAsync(CancellationToken token)
        {
            if (_projection == null || _repository == null)
                return Task.FromResult(Result<UnityLogicalAssetIndex>.Failure(new LoreError(
                    ErrorCode.InvalidRepository, "No verified Unity repository is available.")));
            return _projection.RefreshAsync(_repository, token);
        }

        private static void OnBeforeReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            _changes?.Dispose();
            _editing?.Dispose();
            _editing = null;
            _writes = null;
            _installing = null;
            _initializing = null;
            _detecting = false;
            _paths = null;
            _changes = null;
            _projection = null;
            _repository = null;
            _detected = null;
            _initialStatusError = null;
            Reload.Cancel();
            EditorApplication.delayCall -= OnEditorReady;
            _composition = null;
        }
    }
}
