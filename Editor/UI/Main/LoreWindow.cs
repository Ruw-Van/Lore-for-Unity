using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
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
        private bool _pushAfterCommit = true;
        private bool _busy;
        private int _tab;
        private int _page;
        private long _generation = -1;
        private Lore.Unity.Application.Status.StatusSnapshot _observedStatus;
        private UnityLogicalAssetIndex _observedAssets;
        private Result<IReadOnlyList<PendingRecovery>> _pending;
        private double _nextRecoveryScan;

        [MenuItem("Window/Lore/Lore")]
        public static void Open() => GetWindow<LoreWindow>("Lore");

        private void OnEnable()
        {
            _controller = new LoreWindowController();
            _lifetime = new CancellationTokenSource();
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
            EditorGUILayout.LabelField("Repository", _controller.Repository?.Value ?? "Not detected");
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
                    var conflicted = index.TryGet(path, out var selectedAsset) &&
                        (selectedAsset.Asset?.Status.Conflict == ConflictState.Conflicted ||
                         selectedAsset.Meta?.Status.Conflict == ConflictState.Conflicted);
                    if (conflicted)
                    {
                        var options = _controller.ResolverCandidates(path);
                        EditorGUILayout.HelpBox("Lore native conflict. Resolver priority: " +
                            string.Join(" → ", options) +
                            ". Binary choice discards one version; review Diff before proceeding.",
                            MessageType.Warning);
                        if (_pending.IsSuccess)
                            foreach (var recovery in _pending.Value)
                                if (recovery.Operation == "BranchMerge" && recovery.LoreApplied &&
                                    recovery.Repository.Equals(_controller.Repository))
                                {
                                    DrawChoice(recovery, path, ConflictChoice.Mine, "Keep mine");
                                    DrawChoice(recovery, path, ConflictChoice.Theirs, "Keep theirs");
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
            EditorGUI.BeginDisabledGroup(_busy || _selected.Count == 0 || string.IsNullOrWhiteSpace(_message));
            if (GUILayout.Button("Check In"))
            {
                var paths = new List<RepositoryPath>(_selected);
                var message = _message;
                var push = _pushAfterCommit;
                if (EditorUtility.DisplayDialog("Enable Lore editing and Check In?",
                    "Stage the selected Asset/.meta pairs and create a Lore revision? Unsaved Unity edits are never discarded.",
                    "Check In", "Cancel"))
                    Schedule(() => RunCheckInAsync(paths, message, push));
            }
            EditorGUI.EndDisabledGroup();
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
                var result = await _controller.CheckInAsync(paths, message, push, token);
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
