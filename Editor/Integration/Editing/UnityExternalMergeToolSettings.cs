using System;
using System.Collections.Generic;
using Lore.Unity.Application.Conflicts;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Results;
using UnityEditor;
using UnityEngine;

namespace Lore.Unity.Integration.Editing
{
    [FilePath("UserSettings/LoreForUnityMergeTools.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class UnityExternalMergeToolSettings : ScriptableSingleton<UnityExternalMergeToolSettings>
    {
        [Serializable]
        private sealed class Entry
        {
            public string name;
            public string executable;
            public List<string> arguments;
        }

        [SerializeField] private List<Entry> tools = new List<Entry>();

        public Result<IReadOnlyList<ExternalMergeTool>> Read()
        {
            var result = new List<ExternalMergeTool>();
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (tools == null || tools.Count > 16) return Invalid();
                foreach (var item in tools)
                {
                    if (item == null) return Invalid();
                    var tool = new ExternalMergeTool(item.name, item.executable, item.arguments);
                    if (!names.Add(tool.Name)) return Invalid();
                    result.Add(tool);
                }
                return Result<IReadOnlyList<ExternalMergeTool>>.Success(result.AsReadOnly());
            }
            catch (ArgumentException) { return Invalid(); }
        }

        public void Set(IReadOnlyList<ExternalMergeTool> entries)
        {
            if (entries == null || entries.Count > 16) throw new ArgumentException("Too many tools.", nameof(entries));
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var copy = new List<Entry>();
            foreach (var tool in entries)
            {
                if (tool == null || !names.Add(tool.Name)) throw new ArgumentException("Duplicate tool name.", nameof(entries));
                copy.Add(new Entry { name = tool.Name, executable = tool.Executable,
                    arguments = new List<string>(tool.Arguments) });
            }
            tools = copy;
            Save(true);
        }

        private static Result<IReadOnlyList<ExternalMergeTool>> Invalid() =>
            Result<IReadOnlyList<ExternalMergeTool>>.Failure(new LoreError(ErrorCode.ValidationFailed,
                "Personal merge tool settings are invalid. Open Window > Lore > Settings."));
    }
}
