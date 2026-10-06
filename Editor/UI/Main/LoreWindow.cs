using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Application.CheckIn;
using Lore.Unity.Application.Conflicts;
using Lore.Unity.Application.Diff;
using Lore.Unity.Application.Queries;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Status;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.Recovery;
using Lore.Unity.Integration.Assets;
using Lore.Unity.Integration.EditorLifecycle;
using UnityEditor;
using UnityEngine;

namespace Lore.Unity.UI.Main
{
    public sealed class LoreWindow : EditorWindow
    {
        private static readonly string[] Tabs = { "Changes", "History", "Branches" };
        private readonly HashSet<RepositoryPath> _selected = new HashSet<RepositoryPath>();
        private LoreWindowController _controller;
        private CancellationTokenSource _lifetime;
        private IReadOnlyList<RevisionHistoryEntry> _history;
        private IReadOnlyList<BranchName> _branches;
        private Vector2 _scroll;
        private string _message = string.Empty;
        private string _notice;
        private string _diff;
        private RepositoryPath _diffPath;
        private DiffMode _diffMode = DiffMode.Simple;
        private bool _pushAfterCommit;
        private StageSelectionProgress _stageProgress;
        private bool _staging;
        private CancellationTokenSource _stageCancellation;
        private bool _busy;
        private int _tab;
        private int _page;
        private long _generation = -1;
        private Lore.Unity.Application.Status.StatusSnapshot _observedStatus;
        private UnityLogicalAssetIndex _observedAssets;
        private Result<IReadOnlyList<PendingRecovery>> _pending;
        private double _nextRecoveryScan;
        private readonly List<ExternalMergeTool> _tools = new List<ExternalMergeTool>();
        private string _toolError;
        private ConflictDraft _draft;
        private string _editedText;
        private OperationId _draftRecovery;

        [MenuItem("Window/Lore/Lore")]
        public static void Open() => GetWindow<LoreWindow>("Lore");

        private void OnEnable()
        {
            _controller = new LoreWindowController();
            _lifetime = new CancellationTokenSource();
            LoadTools();
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
        }

        private void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup >= _nextRecoveryScan)
            {
                _nextRecoveryScan = EditorApplication.timeSinceStartup + 2;
                _pending = _controller.PendingRecovery();
                Repaint();
            }
            var status = _controller?.Status;
            var assets = _controller?.Assets;
            if (ReferenceEquals(status, _observedStatus) && ReferenceEquals(assets, _observedAssets)) return;
            _observedStatus = status;
            _observedAssets = assets;
            Repaint(); // O(1) snapshot check; no Lore I/O in repaint or OnGUI.
        }

        private void OnGUI()
        {
            if (_controller == null) return;
            EditorGUILayout.LabelField("Runtime", _controller.Availability.ToString());
            if (_controller.Availability != Lore.Unity.Application.Runtime.RuntimeAvailability.Ready &&
                GUILayout.Button("Open Runtime Setup")) Lore.Unity.UI.Diagnostics.LoreDiagnosticsWindow.Open();
            EditorGUILayout.LabelField("Repository", _controller.Repository?.Value ?? "Not detected");
            if (!_busy && _controller.CanInitializeRepository &&
                GUILayout.Button("Initialize this Unity project with Lore"))
            {
                var root = System.IO.Path.GetDirectoryName(UnityEngine.Application.dataPath);
                if (EditorUtility.DisplayDialog("Initialize local Lore repository?",
                    "Create an offline Lore working copy at:\n" + root +
                    "\n\nExisting project files will not be staged or pushed. The new .lore directory will be kept if verification fails.",
                    "Initialize", "Cancel"))
                    Schedule(() => RunInitializeRepositoryAsync());
            }
            if (_controller.StatusError != null) EditorGUILayout.HelpBox(_controller.StatusError.Message, MessageType.Warning);
            if (_controller.LockError != null) EditorGUILayout.HelpBox(_controller.LockError.Message, MessageType.Warning);
            if (_controller.LastRefreshError != null)
                EditorGUILayout.HelpBox(_controller.LastRefreshError.Message, MessageType.Warning);
            if (_pending.IsSuccess)
                foreach (var item in _pending.Value)
                {
                    EditorGUILayout.HelpBox("Recovery pending: " + item.Operation + " (" + item.Id.Value +
                        "). Inspect the working copy and Lore native state before acknowledging.", MessageType.Warning);
                    if (!_busy && GUILayout.Button("Acknowledge inspected recovery") &&
                        EditorUtility.DisplayDialog("Acknowledge recovery?",
                            "Only continue if you inspected the working copy and resolved or aborted the Lore merge. " +
                            "This does not perform resolution or undo any changes.", "Acknowledge", "Cancel"))
                        Schedule(() => RunAcknowledgeAsync(item));
                }
            if (!string.IsNullOrEmpty(_notice)) EditorGUILayout.HelpBox(_notice, MessageType.Info);
            if (_stageProgress != null)
            {
                var fraction = _stageProgress.Total == 0 ? 0f :
                    (float)_stageProgress.Completed / _stageProgress.Total;
                EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight),
                    fraction, "Staging " + _stageProgress.Completed + " / " + _stageProgress.Total + " assets");
                if (!string.IsNullOrEmpty(_stageProgress.Current.Value))
                    EditorGUILayout.LabelField("Last staged asset", _stageProgress.Current.Value);
                if (_stageCancellation != null && GUILayout.Button("Cancel staging"))
                    _stageCancellation.Cancel();
            }
            _tab = GUILayout.Toolbar(_tab, Tabs);
            switch (_tab)
            {
                case 0: DrawChanges(); break;
                case 1: DrawHistory(); break;
                case 2: DrawBranches(); break;
            }
        }

        private void DrawChanges()
        {
            var snapshot = _controller.Status;
            var index = _controller.Assets;
            if (snapshot == null || index == null)
            {
                EditorGUILayout.HelpBox("No current Lore status is available.", MessageType.Info);
                if (!_busy && GUILayout.Button("Refresh")) Schedule(() => RunRefreshAsync());
                return;
            }
            if (_generation != snapshot.Generation)
            {
                _generation = snapshot.Generation;
                _page = 0;
                var valid = new HashSet<RepositoryPath>();
                foreach (var asset in index.Assets) valid.Add(Key(asset));
                foreach (var file in index.RepositoryFiles) valid.Add(file.Path);
                _selected.RemoveWhere(path => !valid.Contains(path));
            }
            EditorGUILayout.LabelField("Generation", snapshot.Generation.ToString());
            EditorGUILayout.LabelField("Last refresh (UTC)", snapshot.RefreshedUtc.ToString("u"));
            if (!_busy && GUILayout.Button("Refresh")) Schedule(() => RunRefreshAsync());
            if (!_busy && GUILayout.Button("Select all listed changes") &&
                EditorUtility.DisplayDialog("Select all Lore changes?",
                    "Select every listed Asset/.meta pair and repository file (excluding parent directories), " +
                    "including generated files outside Assets? " +
                    "Review the selection before staging.", "Select all", "Cancel"))
            {
                var directories = new HashSet<RepositoryPath>();
                foreach (var entry in snapshot.Entries)
                    for (var slash = entry.Path.Value.IndexOf('/'); slash >= 0;
                         slash = entry.Path.Value.IndexOf('/', slash + 1))
                        directories.Add(new RepositoryPath(entry.Path.Value.Substring(0, slash)));
                foreach (var asset in index.Assets)
                    if (!directories.Contains(Key(asset))) _selected.Add(Key(asset));
                foreach (var file in index.RepositoryFiles)
                    if (!directories.Contains(file.Path)) _selected.Add(file.Path);
            }
            var total = index.Assets.Count + index.RepositoryFiles.Count;
            var first = _page * 200;
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginDisabledGroup(_page == 0);
            if (GUILayout.Button("Previous")) _page--;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.LabelField(total == 0 ? "No changes" :
                (first + 1) + "–" + Math.Min(total, first + 200) + " / " + total);
            EditorGUI.BeginDisabledGroup(first + 200 >= total);
            if (GUILayout.Button("Next")) _page++;
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            var row = 0;
            foreach (var asset in index.Assets)
            {
                if (row++ < first) continue;
                if (row > first + 200) break;
                var key = Key(asset);
                DrawSelection(key, asset.Path.Value + (asset.HasOnlyMetaChange ? " [meta only]" : "") +
                    "  " + Describe(asset.Asset, asset.Meta));
            }
            foreach (var file in index.RepositoryFiles)
            {
                if (row++ < first) continue;
                if (row > first + 200) break;
                DrawSelection(file.Path, file.Path.Value + "  " + Describe(file, null));
            }
            EditorGUILayout.EndScrollView();
            if (_selected.Count == 1)
                foreach (var path in _selected)
                {
                    var hasAsset = index.TryGet(path, out var selectedAsset);
                    FileStatusEntry repositoryFile = null;
                    if (!hasAsset)
                        foreach (var file in index.RepositoryFiles)
                            if (file.Path.Equals(path)) { repositoryFile = file; break; }
                    var conflicted = (hasAsset &&
                        (selectedAsset.Asset?.Status.Conflict == ConflictState.Conflicted ||
                         selectedAsset.Meta?.Status.Conflict == ConflictState.Conflicted)) ||
                         repositoryFile?.Status.Conflict == ConflictState.Conflicted;
                    if (conflicted)
                    {
                        var conflictPath = repositoryFile != null ? repositoryFile.Path :
                            selectedAsset.Asset?.Status.Conflict == ConflictState.Conflicted ?
                                selectedAsset.Asset.Path : selectedAsset.Meta.Path;
                        var options = new ConflictResolverChain(_tools.Count > 0).Candidates(path);
                        EditorGUILayout.HelpBox("Lore native conflict. Resolver priority: " +
                            string.Join(" → ", options) +
                            ". Binary choice discards one version; review Diff before proceeding.",
                            MessageType.Warning);
                        if (_pending.IsSuccess)
                            foreach (var recovery in _pending.Value)
                                if (recovery.Operation == "BranchMerge" && recovery.LoreApplied &&
                                    recovery.Repository.Equals(_controller.Repository))
                                {
                                    if (hasAsset && selectedAsset.Asset?.Status.Conflict == ConflictState.Conflicted &&
                                        selectedAsset.Meta?.Status.Conflict == ConflictState.Conflicted)
                                        EditorGUILayout.HelpBox("Asset and .meta both conflict. Choose a version " +
                                            "for the pair before using text or external resolution.", MessageType.Warning);
                                    else DrawTextResolution(recovery, conflictPath);
                                    DrawChoice(recovery, conflictPath, ConflictChoice.Mine, "Keep mine");
                                    DrawChoice(recovery, conflictPath, ConflictChoice.Theirs, "Keep theirs");
                                }
                    }
                }
            EditorGUILayout.LabelField("Diff mode", _diffMode.ToString());
            if (GUILayout.Button("Toggle Simple / Structured"))
                _diffMode = _diffMode == DiffMode.Simple ? DiffMode.Structured : DiffMode.Simple;
            if (_selected.Count == 1 && !_busy && GUILayout.Button("Show Diff"))
            {
                foreach (var path in _selected) Schedule(() => RunDiffAsync(path, _diffMode));
            }
            if (_diff != null)
            {
                EditorGUILayout.LabelField("Diff", _diffPath.Value);
                EditorGUILayout.TextArea(_diff);
            }
            _message = EditorGUILayout.TextField("Message", _message);
            _pushAfterCommit = EditorGUILayout.Toggle("Push after Check In", _pushAfterCommit);
            var ready = SelectedStaged(snapshot);
            if (_selected.Count > 0 && ready)
                EditorGUILayout.HelpBox("Selected changes are staged. Review them, then Check In when ready.",
                    MessageType.Info);
            EditorGUI.BeginDisabledGroup(_busy || _selected.Count == 0 || ready);
            if (GUILayout.Button("Stage selected changes"))
            {
                var paths = new List<RepositoryPath>(_selected);
                if (EditorUtility.DisplayDialog("Stage selected changes?",
                    "Stage the selected Asset/.meta pairs in bounded batches? This does not create a revision or push. " +
                    "If interrupted, some files may remain staged; refresh and inspect before retrying.",
                    "Stage", "Cancel"))
                    Schedule(() => RunStageAsync(paths));
            }
            EditorGUI.EndDisabledGroup();
            EditorGUI.BeginDisabledGroup(_busy || _selected.Count == 0 || !ready || string.IsNullOrWhiteSpace(_message));
            if (GUILayout.Button("Check In staged selection"))
            {
                var paths = new List<RepositoryPath>(_selected);
                var message = _message;
                var push = _pushAfterCommit;
                if (EditorUtility.DisplayDialog("Check In staged selection?",
                    "Create one Lore revision from the staged selection" +
                    (push ? " and push it to the remote" : " without pushing") +
                    "? Lore state is rechecked before Commit. Unsaved Unity edits are never discarded.",
                    "Check In", "Cancel"))
                    Schedule(() => RunCheckInAsync(paths, message, push));
            }
            EditorGUI.EndDisabledGroup();
        }

        private bool SelectedStaged(Lore.Unity.Application.Status.StatusSnapshot snapshot)
        {
            if (_selected.Count == 0) return false;
            var prefix = _controller.AssetRootPrefix;
            if (prefix == null) return false;
            var selected = new HashSet<RepositoryPath>();
            foreach (var path in _selected) selected.Add(StageSelectionService.Logical(path, prefix));
            var found = new HashSet<RepositoryPath>();
            foreach (var entry in snapshot.Entries)
            {
                var logical = StageSelectionService.Logical(entry.Path, prefix);
                if (!selected.Contains(logical)) continue;
                if (entry.Status.Stage != StageState.Staged) return false;
                found.Add(logical);
            }
            return found.SetEquals(selected);
        }

        private void DrawHistory()
        {
            if (!_busy && GUILayout.Button("Refresh History")) Schedule(() => RunHistoryAsync());
            if (_history == null) { EditorGUILayout.LabelField("History not loaded."); return; }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var item in _history)
            {
                EditorGUILayout.LabelField(item.Number + "  " + item.Message);
                EditorGUILayout.LabelField(item.TimestampUtc.ToString("u") + "  " + item.Revision.Value);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawBranches()
        {
            if (!_busy && GUILayout.Button("Refresh Branches")) Schedule(() => RunBranchesAsync());
            if (_branches == null) { EditorGUILayout.LabelField("Branches not loaded."); return; }
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var branch in _branches)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(branch.Value);
                EditorGUI.BeginDisabledGroup(_busy);
                if (GUILayout.Button("Switch", GUILayout.Width(70)) &&
                    EditorUtility.DisplayDialog("Switch Lore branch?",
                        "Switch to " + branch.Value + "? Unsaved Scenes, Prefab Mode and unsafe working copies are rejected.",
                        "Switch", "Cancel"))
                    Schedule(() => RunSwitchAsync(branch));
                EditorGUI.EndDisabledGroup();
                EditorGUI.BeginDisabledGroup(_busy);
                if (GUILayout.Button("Merge", GUILayout.Width(70)) &&
                    EditorUtility.DisplayDialog("Merge Lore branch?",
                        "Merge " + branch.Value + " into the current branch? Native conflicts require explicit recovery.",
                        "Merge", "Cancel"))
                    Schedule(() => RunMergeAsync(branch));
                EditorGUI.EndDisabledGroup();
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawSelection(RepositoryPath key, string label)
        {
            var selected = EditorGUILayout.ToggleLeft(label, _selected.Contains(key));
            if (selected) _selected.Add(key);
            else _selected.Remove(key);
        }

        private static RepositoryPath Key(UnityLogicalAsset asset) => asset.Asset?.Path ?? asset.Meta.Path;

        private void LoadTools()
        {
            var loaded = _controller.RegisteredTools();
            _tools.Clear();
            _toolError = loaded.IsFailure ? loaded.Error.Message : null;
            if (loaded.IsSuccess) _tools.AddRange(loaded.Value);
        }

        private void DrawTextResolution(PendingRecovery recovery, RepositoryPath path)
        {
            if (_toolError != null) EditorGUILayout.HelpBox(_toolError, MessageType.Warning);
            if (GUILayout.Button("External tool settings")) Lore.Unity.UI.Settings.LoreSettingsWindow.Open();
            if (GUILayout.Button("Reload registered tools")) LoadTools();
            EditorGUI.BeginDisabledGroup(_busy);
            if (GUILayout.Button("Try safe automatic text / Unity YAML resolution") &&
                EditorUtility.DisplayDialog("Resolve text automatically?",
                    "Only unambiguous diff3 blocks will be applied. Asset/.meta joint conflicts are rejected. " +
                    "The result is staged and marked resolved by Lore; inspect before acknowledging recovery.",
                    "Resolve", "Cancel"))
                Schedule(() => RunTextResolutionAsync(recovery.Id, path,
                    new TextResolutionRequest(TextResolutionMode.Automatic)));
            foreach (var tool in _tools)
            {
                if (GUILayout.Button("Resolve with " + tool.Name) &&
                    EditorUtility.DisplayDialog("Run trusted external merge tool?",
                        "Run " + tool.Name + " on temporary copies of this conflict? Its result replaces " +
                        path.Value + " after validation. Inspect the result before acknowledging recovery.",
                        "Run tool", "Cancel"))
                    Schedule(() => RunTextResolutionAsync(recovery.Id, path,
                        new TextResolutionRequest(TextResolutionMode.External, tool: tool)));
            }
            if (GUILayout.Button("Load conflicted text for manual editing"))
                Schedule(() => RunPreviewAsync(recovery.Id, path));
            EditorGUI.EndDisabledGroup();
            if (_draft != null && _draft.Path.Equals(path) && _draftRecovery.Equals(recovery.Id))
            {
                EditorGUILayout.LabelField("Resolution draft (working copy is unchanged until Apply)");
                _editedText = EditorGUILayout.TextArea(_editedText);
                EditorGUI.BeginDisabledGroup(_busy);
                if (GUILayout.Button("Apply edited text") &&
                    EditorUtility.DisplayDialog("Apply edited conflict text?",
                        "Replace, stage and mark this file resolved in Lore? The recovery record remains pending.",
                        "Apply", "Cancel"))
                {
                    var draft = _draft;
                    var text = _editedText;
                    Schedule(() => RunTextResolutionAsync(recovery.Id, path,
                        new TextResolutionRequest(TextResolutionMode.Manual, draft, text)));
                }
                EditorGUI.EndDisabledGroup();
            }
        }

        private void DrawChoice(PendingRecovery pending, RepositoryPath path,
            ConflictChoice choice, string label)
        {
            EditorGUI.BeginDisabledGroup(_busy);
            if (GUILayout.Button(label) && EditorUtility.DisplayDialog("Discard one conflict version?",
                "Lore will choose " + label + " for " + path.Value +
                " and its conflicted .meta. The other version is discarded. Recovery stays pending " +
                "until you inspect and acknowledge it.", "Choose version", "Cancel"))
                Schedule(() => RunChoiceAsync(pending.Id, path, choice));
            EditorGUI.EndDisabledGroup();
        }
        private static string Describe(FileStatusEntry asset, FileStatusEntry meta) =>
            (asset == null ? string.Empty : asset.Status.Working + "/" + asset.Status.Stage) +
            (meta == null ? string.Empty : "  meta:" + meta.Status.Working + "/" + meta.Status.Stage) +
            ((asset?.Status.Conflict == ConflictState.Conflicted ||
              meta?.Status.Conflict == ConflictState.Conflicted) ? "  [LORE CONFLICT]" : string.Empty);

        private void Schedule(Func<Task> action)
        {
            EditorApplication.delayCall += () => { if (_lifetime != null) _ = action(); };
        }

        private async Task RunRefreshAsync() => await RunAsync(async token =>
        {
            var result = await _controller.RefreshAsync(token);
            _notice = result.IsSuccess ? "Status refreshed." : result.Error.Message;
        });

        private async Task RunStageAsync(IReadOnlyList<RepositoryPath> paths) => await RunAsync(async token =>
        {
            _stageProgress = new StageSelectionProgress(0, paths.Count, default);
            _staging = true;
            _stageCancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
            var progress = new Progress<StageSelectionProgress>(value =>
            {
                if (this == null || !_staging || _lifetime == null || _lifetime.IsCancellationRequested) return;
                _stageProgress = value;
                Repaint();
            });
            try
            {
                var result = await _controller.StageSelectionAsync(paths, progress, _stageCancellation.Token);
                _notice = result.IsSuccess ? "Selected changes staged. Review Changes before Check In." :
                    result.Error.Message + " Refresh Lore status and inspect partial stage before retrying.";
            }
            finally
            {
                _staging = false;
                _stageCancellation.Dispose();
                _stageCancellation = null;
                _stageProgress = null;
            }
        });

        private async Task RunInitializeRepositoryAsync() => await RunAsync(async token =>
        {
            var result = await _controller.InitializeRepositoryAsync(token);
            _notice = result.IsSuccess ? "Local Lore repository created and verified. Review Changes before staging." :
                result.Error.Message;
        });

        private async Task RunHistoryAsync() => await RunAsync(async token =>
        {
            var result = await _controller.HistoryAsync(50, token);
            if (result.IsSuccess) _history = result.Value;
            _notice = result.IsSuccess ? null : result.Error.Message;
        });

        private async Task RunBranchesAsync() => await RunAsync(async token =>
        {
            var result = await _controller.BranchesAsync(token);
            if (result.IsSuccess) _branches = result.Value;
            _notice = result.IsSuccess ? null : result.Error.Message;
        });

        private async Task RunCheckInAsync(IReadOnlyList<RepositoryPath> paths, string message, bool push) =>
            await RunAsync(async token =>
            {
                var result = await _controller.CommitStagedAsync(paths, message, push, token);
                if (result.IsFailure) { _notice = result.Error.Message; return; }
                var outcome = result.Value;
                _notice = outcome.IsCommitted
                    ? outcome.Push.HasValue && outcome.Push.Value.IsFailure
                        ? "Commit succeeded; Push failed: " + outcome.Push.Value.Error.Message
                        : outcome.PostCommitStatus.HasValue && outcome.PostCommitStatus.Value.IsFailure
                            ? "Commit succeeded; Status refresh failed: " + outcome.PostCommitStatus.Value.Error.Message
                            : "Commit succeeded."
                    : outcome.CommitOutcomeUnknown
                        ? "Commit outcome is unknown. Refresh Lore status before retry: " + outcome.Commit.Error.Message
                        : "Check In failed: " + outcome.Commit.Error.Message;
                if (outcome.IsCommitted) _selected.Clear();
            });

        private async Task RunSwitchAsync(BranchName branch) => await RunAsync(async token =>
        {
            var result = await _controller.SwitchAsync(branch, token);
            _notice = result.IsFailure ? result.Error.Message : result.Value.Result.IsFailure
                ? "Branch switch failed: " + result.Value.Result.Error.Message
                : "Branch switched.";
        });

        private async Task RunMergeAsync(BranchName branch) => await RunAsync(async token =>
        {
            var result = await _controller.MergeAsync(branch, token);
            _notice = result.IsFailure ? result.Error.Message : result.Value.Result.IsFailure
                ? "Merge requires attention (" + result.Value.Id.Value + "): " + result.Value.Result.Error.Message
                : "Merge completed.";
        });

        private async Task RunDiffAsync(RepositoryPath path, DiffMode mode) => await RunAsync(async token =>
        {
            var result = await _controller.DiffAsync(path, mode, token);
            _diffPath = path;
            _diff = result.IsSuccess ? result.Value : null;
            _notice = result.IsFailure ? result.Error.Message : null;
        });

        private async Task RunAcknowledgeAsync(Lore.Unity.Infrastructure.Recovery.PendingRecovery pending) =>
            await RunAsync(async token =>
            {
                var result = await _controller.AcknowledgeRecoveryAsync(pending, token);
                _notice = result.IsSuccess ? "Recovery record acknowledged after Lore status refresh." :
                    result.Error.Message;
            });

        private async Task RunChoiceAsync(OperationId id, RepositoryPath path,
            ConflictChoice choice) => await RunAsync(async token =>
        {
            var result = await _controller.ChooseVersionAsync(id, path, choice, token);
            _notice = result.IsFailure ? result.Error.Message : result.Value.Result.IsFailure
                ? "Lore resolution requires inspection: " + result.Value.Result.Error.Message
                : "Lore resolved the selected file. Inspect remaining conflicts before acknowledging recovery.";
        });

        private async Task RunPreviewAsync(OperationId id, RepositoryPath path) => await RunAsync(async token =>
        {
            var result = await _controller.PreviewConflictAsync(id, path, token);
            _draft = null;
            if (result.IsFailure) { _notice = result.Error.Message; return; }
            if (result.Value.Original.Length > 256000)
            { _notice = "Conflict text is too large for the Editor; try a registered external tool."; return; }
            _draft = result.Value;
            _draftRecovery = id;
            _editedText = result.Value.Original;
            _notice = "Remove conflict markers and review the text before applying.";
        });

        private async Task RunTextResolutionAsync(OperationId id, RepositoryPath path,
            TextResolutionRequest request) => await RunAsync(async token =>
        {
            var result = await _controller.ResolveTextAsync(id, path, request, token);
            _notice = result.IsFailure ? result.Error.Message : result.Value.Result.IsFailure
                ? "Resolution requires inspection: " + result.Value.Result.Error.Message
                : "Lore staged and resolved this file. Inspect changes before acknowledging recovery.";
            if (result.IsSuccess && result.Value.Result.IsSuccess) _draft = null;
        });

        private async Task RunAsync(Func<CancellationToken, Task> operation)
        {
            if (_busy || _lifetime == null) return;
            _busy = true;
            try { await operation(_lifetime.Token); }
            catch (OperationCanceledException) { _notice = "Operation cancelled; re-query Lore before retry."; }
            catch (Exception error) { _notice = "Operation failed: " + error.GetType().Name; }
            finally
            {
                _busy = false;
                _nextRecoveryScan = 0;
                if (this != null) Repaint();
            }
        }
    }
}
