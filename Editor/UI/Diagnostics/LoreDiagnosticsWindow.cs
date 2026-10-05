using System.Runtime.InteropServices;
using Lore.Unity.Application.Diagnostics;
using Lore.Unity.Integration.EditorLifecycle;
using UnityEditor;
using UnityEngine;

namespace Lore.Unity.UI.Diagnostics
{
    public sealed class LoreDiagnosticsWindow : EditorWindow
    {
        private LoreWindowController _controller;
        private DiagnosticsSummary _snapshot;

        [MenuItem("Window/Lore/Diagnostics")]
        public static void Open() => GetWindow<LoreDiagnosticsWindow>("Lore Diagnostics");

        private void OnEnable()
        {
            _controller = new LoreWindowController();
            Refresh();
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
    }
}
