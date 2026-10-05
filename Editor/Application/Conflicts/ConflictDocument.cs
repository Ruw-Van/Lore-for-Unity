using System;
using System.Collections.Generic;
using System.Text;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Conflicts
{
    // Parses Lore diff3 markers, preserving all surrounding bytes represented as
    // UTF-8 text. An incomplete or nested marker block is never auto-resolved.
    public sealed class ConflictDocument
    {
        private sealed class Region
        {
            public string Mine;
            public string Base;
            public string Theirs;
        }

        private readonly List<object> _parts;
        private ConflictDocument(List<object> parts) { _parts = parts; }

        public static Result<ConflictDocument> Parse(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length > 8 * 1024 * 1024 || content.IndexOf('\0') >= 0)
                return Invalid();
            var parts = new List<object>();
            var ordinary = new StringBuilder();
            var side = new StringBuilder();
            var state = 0;
            var conflicts = 0;
            var region = new Region();
            for (var start = 0; start < content.Length;)
            {
                var end = content.IndexOf('\n', start);
                end = end < 0 ? content.Length : end + 1;
                var line = content.Substring(start, end - start);
                var marker = line.TrimEnd('\r', '\n');
                start = end;
                if (marker.StartsWith("<<<<<<<", StringComparison.Ordinal) ||
                    marker.StartsWith("|||||||", StringComparison.Ordinal) ||
                    marker == "=======" || marker.StartsWith(">>>>>>>", StringComparison.Ordinal))
                {
                    if (state == 0 && marker == "<<<<<<< ours")
                    {
                        if (ordinary.Length > 0) { parts.Add(ordinary.ToString()); ordinary.Clear(); }
                        region = new Region();
                        state = 1;
                    }
                    else if (state == 1 && marker == "||||||| original")
                    { region.Mine = side.ToString(); side.Clear(); state = 2; }
                    else if (state == 2 && marker == "=======")
                    { region.Base = side.ToString(); side.Clear(); state = 3; }
                    else if (state == 3 && marker == ">>>>>>> theirs")
                    {
                        region.Theirs = side.ToString(); side.Clear();
                        parts.Add(region);
                        conflicts++;
                        state = 0;
                    }
                    else return Invalid();
                }
                else if (state == 0) ordinary.Append(line);
                else side.Append(line);
            }
            if (state != 0 || conflicts == 0) return Invalid();
            if (ordinary.Length > 0) parts.Add(ordinary.ToString());
            return Result<ConflictDocument>.Success(new ConflictDocument(parts));
        }

        public string Version(int side)
        {
            if (side < 0 || side > 2) throw new ArgumentOutOfRangeException(nameof(side));
            var output = new StringBuilder();
            foreach (var part in _parts)
                output.Append(part is Region region ? side == 0 ? region.Base :
                    side == 1 ? region.Mine : region.Theirs : (string)part);
            return output.ToString();
        }

        public Result<string> AutoResolve(bool unityYaml)
        {
            var output = new StringBuilder();
            foreach (var part in _parts)
            {
                if (!(part is Region region)) { output.Append((string)part); continue; }
                if (region.Mine == region.Theirs) output.Append(region.Mine);
                else if (region.Mine == region.Base) output.Append(region.Theirs);
                else if (region.Theirs == region.Base) output.Append(region.Mine);
                else if (unityYaml && TryMergeFields(region, out var merged)) output.Append(merged);
                else return Result<string>.Failure(new LoreError(ErrorCode.Conflict,
                    "Both sides changed the same text; resolve manually or use a configured merge tool."));
            }
            return Result<string>.Success(output.ToString());
        }

        public static bool ContainsMarkers(string content) => content != null &&
            (content.Contains("<<<<<<<") || content.Contains("|||||||") ||
             content.Contains(">>>>>>>"));

        private static bool TryMergeFields(Region region, out string merged)
        {
            merged = null;
            if (!Fields(region.Base, out var original, out var order) ||
                !Fields(region.Mine, out var mine, out var mineOrder) ||
                !Fields(region.Theirs, out var theirs, out var theirOrder)) return false;
            var keys = new List<string>(order);
            foreach (var key in mineOrder) if (!keys.Contains(key)) keys.Add(key);
            foreach (var key in theirOrder) if (!keys.Contains(key)) keys.Add(key);
            var output = new StringBuilder();
            foreach (var key in keys)
            {
                original.TryGetValue(key, out var baseline);
                mine.TryGetValue(key, out var left);
                theirs.TryGetValue(key, out var right);
                if (left != baseline && right != baseline && left != right) return false;
                var chosen = left != baseline ? left : right != baseline ? right : baseline;
                if (chosen != null) output.Append(chosen);
            }
            merged = output.ToString();
            return true;
        }

        private static bool Fields(string content, out Dictionary<string, string> values, out List<string> order)
        {
            values = new Dictionary<string, string>(StringComparer.Ordinal);
            order = new List<string>();
            var indent = -1;
            foreach (var line in content.Split(new[] { '\n' }, StringSplitOptions.None))
            {
                if (line.Length == 0) continue;
                var raw = line.TrimEnd('\r');
                var spaces = 0;
                while (spaces < raw.Length && raw[spaces] == ' ') spaces++;
                var colon = raw.IndexOf(": ", spaces, StringComparison.Ordinal);
                if (spaces == 0 || colon <= spaces || (indent >= 0 && indent != spaces)) return false;
                for (var i = spaces; i < colon; i++)
                    if (!(char.IsLetterOrDigit(raw[i]) || raw[i] == '_')) return false;
                indent = spaces;
                var key = raw.Substring(spaces, colon - spaces);
                if (!values.TryAdd(key, line + "\n")) return false;
                order.Add(key);
            }
            return true;
        }

        private static Result<ConflictDocument> Invalid() => Result<ConflictDocument>.Failure(
            new LoreError(ErrorCode.ValidationFailed, "Unsupported or incomplete Lore diff3 conflict markers."));
    }
}
