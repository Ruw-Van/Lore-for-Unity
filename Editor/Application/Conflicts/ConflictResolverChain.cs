using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lore.Unity.Core.Paths;

namespace Lore.Unity.Application.Conflicts
{
    public enum ResolverKind
    {
        UnityStructured,
        ExternalTool,
        Text,
        BinaryChooseVersion
    }

    // Discovery only. Applying a resolver is a separate, confirmed operation.
    // In particular, a YAML preview is never treated as a successful merge.
    public sealed class ConflictResolverChain
    {
        private readonly bool _externalToolConfigured;

        public ConflictResolverChain(bool externalToolConfigured = false) =>
            _externalToolConfigured = externalToolConfigured;

        public IReadOnlyList<ResolverKind> Candidates(RepositoryPath path)
        {
            if (string.IsNullOrEmpty(path.Value)) throw new ArgumentException("Path required.", nameof(path));
            var name = path.Value;
            var candidates = new List<ResolverKind>();
            var structured = HasExtension(name, ".unity", ".prefab", ".asset");
            var text = structured || HasExtension(name, ".meta", ".cs", ".shader", ".json",
                ".txt", ".yaml", ".yml", ".asmdef", ".uxml", ".uss", ".mat");
            if (structured) candidates.Add(ResolverKind.UnityStructured);
            if (_externalToolConfigured) candidates.Add(ResolverKind.ExternalTool);
            if (text) candidates.Add(ResolverKind.Text);
            candidates.Add(ResolverKind.BinaryChooseVersion);
            return new ReadOnlyCollection<ResolverKind>(candidates);
        }

        private static bool HasExtension(string path, params string[] extensions)
        {
            foreach (var extension in extensions)
                if (path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}
