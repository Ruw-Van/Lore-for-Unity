using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Diff
{
    // Conservative grouping of Unity's text-serialized object changes. A hunk
    // lacking an object anchor is never attributed to the preceding object.
    public sealed class UnityYamlStructuredDiff : IStructuredDiffBackend
    {
        private readonly IDiffBackend _source;
        public UnityYamlStructuredDiff(IDiffBackend source) =>
            _source = source ?? throw new ArgumentNullException(nameof(source));

        public async Task<Result<string>> ReadAsync(RepositoryId repository, RepositoryPath path, CancellationToken token)
        {
            var name = path.Value;
            if (!(name.EndsWith(".unity", StringComparison.OrdinalIgnoreCase) ||
                  name.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                  name.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))) return Unsupported();
            var patch = await _source.ReadTextAsync(repository, path, token);
            return patch.IsFailure ? patch : Format(patch.Value);
        }

        public static Result<string> Format(string patch)
        {
            if (string.IsNullOrEmpty(patch)) return Unsupported();
            var objects = new List<string>();
            var group = new StringBuilder();
            string anchor = null;
            var inHunk = false;
            var changed = false;
            foreach (var line in patch.Split('\n'))
            {
                if (line.StartsWith("@@ ", StringComparison.Ordinal))
                {
                    if (changed && anchor == null) return Unsupported();
                    if (group.Length > 0) objects.Add(group.ToString());
                    group.Clear();
                    anchor = null;
                    changed = false;
                    inHunk = true;
                    continue;
                }
                if (!inHunk) continue;
                if (line.Length < 2 || (line[0] != ' ' && line[0] != '+' && line[0] != '-')) continue;
                var content = line.Substring(1).TrimEnd('\r');
                if (content.StartsWith("--- !u!", StringComparison.Ordinal))
                {
                    if (changed && anchor == null) return Unsupported();
                    if (group.Length > 0) objects.Add(group.ToString());
                    group.Clear();
                    anchor = content;
                    group.AppendLine(anchor);
                    changed = false;
                }
                else if (line[0] == '+' || line[0] == '-')
                {
                    if (anchor == null) return Unsupported();
                    changed = true;
                    group.AppendLine(line[0] + content);
                }
            }
            if (changed && anchor == null) return Unsupported();
            if (group.Length > 0) objects.Add(group.ToString());
            if (objects.Count == 0) return Unsupported();
            var result = new StringBuilder("Unity YAML object changes (text only; review the full Diff before resolving):\n");
            foreach (var item in objects) result.AppendLine(item);
            return Result<string>.Success(result.ToString());
        }

        private static Result<string> Unsupported() => Result<string>.Failure(new LoreError(
            ErrorCode.UnsupportedOperation, "This diff cannot be safely grouped by Unity YAML object. Use Simple Diff."));
    }
}
