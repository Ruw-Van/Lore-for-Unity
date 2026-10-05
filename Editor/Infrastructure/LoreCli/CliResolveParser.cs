using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.LoreCli
{
    public sealed class CliResolveParser
    {
        [DataContract]
        private sealed class Event
        {
            [DataMember(Name = "tagName")] public string Tag { get; set; }
            [DataMember(Name = "data")] public Data Data { get; set; }
        }
        [DataContract]
        private sealed class Data
        {
            [DataMember(Name = "path")] public string Path { get; set; }
            [DataMember(Name = "repository")] public string Repository { get; set; }
            [DataMember(Name = "revision")] public string Revision { get; set; }
            [DataMember(Name = "status")] public int? Status { get; set; }
        }

        public Result Parse(RepositoryId repository, IReadOnlyList<RepositoryPath> paths, string text)
        {
            if (repository == null || paths == null) throw new ArgumentNullException(
                repository == null ? nameof(repository) : nameof(paths));
            if (string.IsNullOrWhiteSpace(text) || paths.Count == 0) return Invalid();
            var expected = new HashSet<string>(StringComparer.Ordinal);
            foreach (var path in paths) expected.Add(path.Value);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var revision = false;
            var complete = false;
            try
            {
                foreach (var line in text.TrimEnd('\r', '\n').Split('\n'))
                {
                    if (line.Length > 65536 || string.IsNullOrWhiteSpace(line)) return Invalid();
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                    {
                        var item = (Event)new DataContractJsonSerializer(typeof(Event)).ReadObject(stream);
                        if (item?.Tag == null || stream.Position != stream.Length || complete) return Invalid();
                        switch (item.Tag)
                        {
                            case "branchMergeResolveFile":
                                if (revision || !expected.Contains(item.Data?.Path) || !seen.Add(item.Data.Path))
                                    return Invalid();
                                break;
                            case "branchMergeResolveRevision":
                                if (revision || item.Data?.Repository != repository.Value ||
                                    !Hex(item.Data.Revision, 64)) return Invalid();
                                revision = true;
                                break;
                            case "complete":
                                if (!revision || seen.Count != expected.Count || item.Data?.Status != 0) return Invalid();
                                complete = true;
                                break;
                            case "log": break;
                            default: return Invalid();
                        }
                    }
                }
                return complete ? Result.Success() : Invalid();
            }
            catch (Exception e) when (e is SerializationException || e is ArgumentException || e is InvalidCastException)
            { return Invalid(); }
        }

        private static bool Hex(string value, int count)
        {
            if (value == null || value.Length != count) return false;
            foreach (var c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        private static Result Invalid() => Result.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Lore CLI returned an incomplete conflict resolution stream; refresh status before retry."));
    }
}
