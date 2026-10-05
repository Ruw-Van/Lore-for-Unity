using System;
using System.Collections.Generic;
using Lore.Unity.Application.Conflicts;
using Lore.Unity.Integration.Editing;
using UnityEditor;
using UnityEngine;

namespace Lore.Unity.UI.Settings
{
    public sealed class LoreSettingsWindow : EditorWindow
    {
        private readonly List<ExternalMergeTool> _tools = new List<ExternalMergeTool>();
        private int _selected = -1;
        private string _name = string.Empty;
        private string _executable = string.Empty;
        private string _arguments = "--base={base}\n--mine={mine}\n--theirs={theirs}\n--result={result}";
        private string _error;

        [MenuItem("Window/Lore/Settings")]
        public static void Open() => GetWindow<LoreSettingsWindow>("Lore Settings");

        private void OnEnable()
        {
            var loaded = UnityExternalMergeToolSettings.instance.Read();
            if (loaded.IsFailure) { _error = loaded.Error.Message; return; }
            _tools.Clear();
            _tools.AddRange(loaded.Value);
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("External merge tools are personal to this Unity project (UserSettings). " +
                "Use trusted executables only. Do not enter tokens, credentials or secrets in arguments. " +
                "Each line is one argument; no shell is used.", MessageType.Warning);
            if (_error != null) EditorGUILayout.HelpBox(_error, MessageType.Warning);
            for (var i = 0; i < _tools.Count; i++)
            {
                var index = i;
                if (GUILayout.Button(_tools[i].Name + (_selected == i ? " (selected)" : ""))) Select(index);
            }
            if (GUILayout.Button("New tool")) Select(-1);
            _name = EditorGUILayout.TextField("Display name", _name);
            _executable = EditorGUILayout.TextField("Executable (absolute)", _executable);
            if (GUILayout.Button("Browse executable"))
            {
                var selected = EditorUtility.OpenFilePanel("Select trusted merge tool", "", "");
                if (!string.IsNullOrEmpty(selected)) _executable = selected;
            }
            EditorGUILayout.LabelField("Arguments (one token per line; {base}, {mine}, {theirs}, {result} required)");
            _arguments = EditorGUILayout.TextArea(_arguments);
            if (GUILayout.Button(_selected < 0 ? "Add tool" : "Save tool")) SaveEntry();
            if (_selected >= 0 && GUILayout.Button("Remove selected tool") &&
                EditorUtility.DisplayDialog("Remove external tool?", _tools[_selected].Name,
                    "Remove", "Cancel"))
            {
                var copy = new List<ExternalMergeTool>(_tools);
                copy.RemoveAt(_selected);
                if (Persist(copy)) Select(-1);
            }
        }

        private void Select(int index)
        {
            _selected = index;
            _error = null;
            if (index < 0)
            {
                _name = _executable = string.Empty;
                _arguments = "--base={base}\n--mine={mine}\n--theirs={theirs}\n--result={result}";
                return;
            }
            var tool = _tools[index];
            _name = tool.Name;
            _executable = tool.Executable;
            _arguments = string.Join("\n", tool.Arguments);
        }

        private void SaveEntry()
        {
            try
            {
                var tokens = _arguments.Replace("\r", string.Empty).Split('\n');
                var tool = new ExternalMergeTool(_name, _executable, tokens);
                var index = _selected < 0 ? _tools.Count : _selected;
                var copy = new List<ExternalMergeTool>(_tools);
                if (_selected < 0) copy.Add(tool);
                else copy[index] = tool;
                if (Persist(copy)) _selected = index;
            }
            catch (ArgumentException error) { _error = error.Message; }
        }

        private bool Persist(List<ExternalMergeTool> copy)
        {
            try
            {
                UnityExternalMergeToolSettings.instance.Set(copy);
                _tools.Clear();
                _tools.AddRange(copy);
                _error = null;
                return true;
            }
            catch (ArgumentException error) { _error = error.Message; return false; }
        }
    }
}
