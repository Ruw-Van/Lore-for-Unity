using System.Runtime.InteropServices;
using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Runtime;
using Lore.Unity.Core.Paths;
using Lore.Unity.Application.Diagnostics;
using Lore.Unity.Infrastructure.Runtime;
using Lore.Unity.Integration.EditorLifecycle;
using UnityEditor;
using UnityEngine;

namespace Lore.Unity.UI.Diagnostics
{
    public sealed class LoreDiagnosticsWindow : EditorWindow
    {
        private LoreWindowController _controller;
        private DiagnosticsSummary _snapshot;
        private CancellationTokenSource _lifetime;
        private bool _installing;
        private string _notice;
        private string _phase;
        private RuntimeInstallProgress _progress;

        [MenuItem("Window/Lore/Diagnostics")]
        public static void Open() => GetWindow<LoreDiagnosticsWindow>("Lore Diagnostics");

        private void OnEnable()
        {
            _controller = new LoreWindowController();
            _lifetime?.Dispose();
            _lifetime = new CancellationTokenSource();
            Refresh();
        }

        private void OnDisable()
        {
            _lifetime?.Cancel();
            _lifetime?.Dispose();
            _lifetime = null;
        }

        private void Refresh() => _snapshot = _controller.Diagnostics();

        private void OnGUI()
        {
            if (_controller == null) return;
            if (GUILayout.Button("Refresh Diagnostics")) Refresh();
            EditorGUILayout.LabelField("Unity", UnityEngine.Application.unityVersion);
            EditorGUILayout.LabelField("OS", RuntimeInformation.OSDescription);
            EditorGUILayout.LabelField("Architecture", RuntimeInformation.OSArchitecture.ToString());
            EditorGUILayout.LabelField("Runtime health", _controller.Availability.ToString());
            DrawRuntimeSetup();
            EditorGUILayout.LabelField("Required Lore", _controller.RequiredVersion ?? "Unavailable");
            EditorGUILayout.LabelField("Installed Lore", _controller.Availability.ToString() == "Ready" ?
                _controller.RequiredVersion : "Not verified");
            var detected = _controller.DetectedRepository;
            if (detected != null)
            {
                EditorGUILayout.LabelField("Repository ID", detected.Id.Value);
                EditorGUILayout.LabelField("Root", detected.Root.Value);
                EditorGUILayout.LabelField("Branch (at detection)", detected.Branch.Value);
                EditorGUILayout.LabelField("Local revision (at detection)", detected.Revision.Value);
            }
            EditorGUILayout.LabelField("Remote revision", "Unavailable without a fresh remote query");
            EditorGUILayout.LabelField("Read backend", _controller.ReadAvailable ? "Verified CLI" : "Unavailable");
            EditorGUILayout.LabelField("Write backend", _controller.WriteEnabled ? "Enabled CLI" : "Not enabled");
            EditorGUILayout.TextArea(_snapshot.Export());
            if (GUILayout.Button("Copy Diagnostics (redacted)"))
                EditorGUIUtility.systemCopyBuffer = _snapshot.Export();
            EditorGUILayout.HelpBox("This export excludes repository paths, IDs, branch names, revisions, logs and secrets. " +
                "Inspect recovery records and the Lore native state before retrying writes.", MessageType.Info);
        }

        private void DrawRuntimeSetup()
        {
            if (_notice != null) EditorGUILayout.HelpBox(_notice, MessageType.Info);
            if (_installing)
            {
                EditorGUILayout.HelpBox(_progress == null ? _phase : StageLabel(_progress.Stage), MessageType.Info);
                if (_progress?.Stage == RuntimeInstallStage.Downloading && _progress.TotalBytes > 0)
                {
                    var fraction = Mathf.Clamp01((float)_progress.BytesReceived / _progress.TotalBytes);
                    var label = string.Format("{0:F1} / {1:F1} MiB ({2:F0}%)",
                        _progress.BytesReceived / 1048576.0, _progress.TotalBytes / 1048576.0,
                        fraction * 100);
                    EditorGUI.ProgressBar(EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight),
                        fraction, label);
                }
            }
            if (_controller.Availability == RuntimeAvailability.Ready) return;
            var artifact = _controller.RuntimeArtifact;
            if (artifact.IsFailure)
            {
                EditorGUILayout.HelpBox("Runtime installation requires a valid manifest and supported host.",
                    MessageType.Warning);
                return;
            }
            EditorGUILayout.LabelField("Pinned archive", artifact.Value.OfficialArtifactUrl.ToString());
            EditorGUILayout.LabelField("Archive size", artifact.Value.DownloadSize.ToString() + " bytes");
            EditorGUI.BeginDisabledGroup(_installing);
            if (GUILayout.Button("Download and install official Lore Runtime") &&
                EditorUtility.DisplayDialog("Install Lore Runtime?",
                    "Download the pinned Lore " + _controller.RequiredVersion + " release from GitHub, verify its " +
                    "size and SHA-256, then install to your user cache? No download occurs at Editor startup.",
                    "Download and install", "Cancel"))
                Schedule((ct, progress) => _controller.InstallRuntimeOfficialAsync(ct, progress),
                    "Connecting to the official Lore release…");
            if (GUILayout.Button("Install Lore Runtime from local archive"))
            {
                var platform = RuntimeComposition.CurrentPlatform();
                var file = EditorUtility.OpenFilePanel("Choose the official Lore archive", "",
                    platform == "Windows-x64" ? "zip" : "gz");
                if (!string.IsNullOrEmpty(file) && EditorUtility.DisplayDialog("Install local Lore archive?",
                    "Verify and install the selected archive into your user cache? The source file is preserved.",
                    "Verify and install", "Cancel"))
                    Schedule((ct, progress) => _controller.InstallRuntimeFromFileAsync(new AbsolutePath(file), ct, progress),
                        "Verifying and installing local Lore archive…");
            }
            EditorGUI.EndDisabledGroup();
        }

        private static string StageLabel(RuntimeInstallStage stage)
        {
            switch (stage)
            {
                case RuntimeInstallStage.Downloading: return "Downloading Lore Runtime archive…";
                case RuntimeInstallStage.PreparingArchive: return "Preparing archive…";
                case RuntimeInstallStage.VerifyingArchive: return "Verifying archive SHA-256…";
                case RuntimeInstallStage.Installing: return "Extracting and checking Lore Runtime…";
                case RuntimeInstallStage.VerifyingRuntime: return "Verifying installed Lore Runtime…";
                case RuntimeInstallStage.Activating: return "Activating Lore Runtime…";
                default: return "Installing Lore Runtime…";
            }
        }

        private void Schedule(Func<CancellationToken, IProgress<RuntimeInstallProgress>,
            Task<Lore.Unity.Core.Results.Result>> action, string phase)
        {
            EditorApplication.delayCall += () =>
            {
                if (_lifetime == null || _installing) return;
                _ = RunInstallAsync(action, phase);
            };
        }

        private async Task RunInstallAsync(Func<CancellationToken, IProgress<RuntimeInstallProgress>,
            Task<Lore.Unity.Core.Results.Result>> action, string phase)
        {
            var lifetime = _lifetime;
            if (lifetime == null) return;
            _installing = true;
            _phase = phase;
            _progress = null;
            _notice = null;
            Repaint();
            try
            {
                // Progress<T> marshals reports from the HTTP copy back to the Editor thread.
                var progress = new Progress<RuntimeInstallProgress>(value =>
                {
                    if (this == null || _lifetime != lifetime || !_installing) return;
                    _progress = value;
                    Repaint();
                });
                var installed = await action(lifetime.Token, progress);
                if (this == null || _lifetime != lifetime) return;
                _notice = installed.IsSuccess ? "Lore Runtime installed and verified." : installed.Error.Message;
                Refresh();
            }
            catch (OperationCanceledException)
            { if (this != null && _lifetime == lifetime) _notice = "Runtime installation was cancelled."; }
            catch (Exception error)
            { if (this != null && _lifetime == lifetime) _notice = "Runtime installation failed: " + error.GetType().Name; }
            finally
            {
                if (this != null && _lifetime == lifetime) { _installing = false; _progress = null; Repaint(); }
            }
        }
    }
}
