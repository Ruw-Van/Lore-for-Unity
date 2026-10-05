using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.LoreCli
{
    public sealed class CliMergeParser
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
            [DataMember(Name = "branch")] public string Branch { get; set; }
            [DataMember(Name = "signature")] public string Signature { get; set; }
            [DataMember(Name = "hasConflicts")] public int? HasConflicts { get; set; }
            [DataMember(Name = "path")] public string Path { get; set; }
            [DataMember(Name = "status")] public int? Status { get; set; }
        }

        public Result Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Invalid();
            var begun = false;
            var ended = false;
            var complete = false;
            var conflicts = 0;
            int? hasConflicts = null;
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
                            case "branchMergeStartBegin":
                                if (begun || !Hex(item.Data?.Branch, 32)) return Invalid();
                                begun = true;
                                break;
                            case "branchMergeStartEnd":
                                if (!begun || ended || !Hex(item.Data?.Signature, 64) ||
                                    (item.Data.HasConflicts != 0 && item.Data.HasConflicts != 1)) return Invalid();
                                ended = true;
                                hasConflicts = item.Data.HasConflicts;
                                break;
                            case "branchMergeConflictFile":
                                if (!begun || string.IsNullOrEmpty(item.Data?.Path)) return Invalid();
                                conflicts++;
                                break;
                            case "complete":
                                if (!begun || !ended || item.Data?.Status != 0) return Invalid();
                                complete = true;
                                break;
                            case "log":
                            case "revisionSyncProgress":
                            case "revisionCommitProgress":
                            case "revisionCommitEnd":
                                break;
                            default: return Invalid();
                        }
                    }
                }
            }
            catch (Exception e) when (e is SerializationException || e is InvalidCastException || e is ArgumentException)
            { return Invalid(); }
            if (!complete || (hasConflicts == 0 && conflicts != 0))
                return Invalid();
            return hasConflicts == 1 ? Result.Failure(new LoreError(ErrorCode.Conflict,
                "Lore merge has unresolved native conflicts; inspect status before resolving.")) : Result.Success();
        }

        private static bool Hex(string value, int count)
        {
            if (value == null || value.Length != count) return false;
            foreach (var c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }
        private static Result Invalid() => Result.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Lore CLI returned an unsupported or incomplete merge event stream."));
    }
}
