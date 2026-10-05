using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Lore.Unity.Application.Queries;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.LoreCli
{
    public sealed class CliHistoryParser
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
            [DataMember(Name = "repository")] public string Repository { get; set; }
            [DataMember(Name = "revision")] public string Revision { get; set; }
            [DataMember(Name = "revisionNumber")] public long? Number { get; set; }
            [DataMember(Name = "key")] public string Key { get; set; }
            [DataMember(Name = "status")] public int? Status { get; set; }
        }
        [DataContract]
        private sealed class StringMetadata
        {
            [DataMember(Name = "data")] public StringMetadataData Data { get; set; }
        }
        [DataContract]
        private sealed class StringMetadataData
        {
            [DataMember(Name = "value")] public StringValue Value { get; set; }
        }
        [DataContract]
        private sealed class StringValue
        {
            [DataMember(Name = "tagName")] public string Tag { get; set; }
            [DataMember(Name = "data")] public string Value { get; set; }
        }
        [DataContract]
        private sealed class NumericMetadata
        {
            [DataMember(Name = "data")] public NumericMetadataData Data { get; set; }
        }
        [DataContract]
        private sealed class NumericMetadataData
        {
            [DataMember(Name = "value")] public NumericValue Value { get; set; }
        }
        [DataContract]
        private sealed class NumericValue
        {
            [DataMember(Name = "tagName")] public string Tag { get; set; }
            [DataMember(Name = "data")] public long? Value { get; set; }
        }

        public Result<IReadOnlyList<RevisionHistoryEntry>> Parse(RepositoryId repository, string text)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (string.IsNullOrWhiteSpace(text)) return Invalid();
            var result = new List<RevisionHistoryEntry>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            string revision = null;
            long? number = null;
            string message = null;
            long? timestamp = null;
            var header = false;
            var completed = false;
            var relayFailed = false;
            try
            {
                foreach (var line in text.TrimEnd('\r', '\n').Split('\n'))
                {
                    if (line.Length > 65536 || string.IsNullOrWhiteSpace(line)) return Invalid();
                    var item = Decode<Event>(line);
                    if (item?.Tag == null) return Invalid();
                    if (completed)
                    {
                        if (item.Tag == "log" && !relayFailed) continue;
                        if (item.Tag == "complete" && !relayFailed && item.Data?.Status > 0)
                        { relayFailed = true; continue; }
                        return Invalid();
                    }
                    switch (item.Tag)
                    {
                        case "revisionHistory":
                            if (header || item.Data?.Repository != repository.Value) return Invalid();
                            header = true;
                            break;
                        case "revisionHistoryEntry":
                            if (!header || !FinishPending()) return Invalid();
                            revision = item.Data?.Revision;
                            number = item.Data?.Number;
                            if (!Hex(revision, 64) || number == null || number < 0 || !seen.Add(revision)) return Invalid();
                            break;
                        case "metadata":
                            if (revision == null || item.Data == null) return Invalid();
                            if (item.Data.Key == "message")
                            {
                                var value = Decode<StringMetadata>(line)?.Data?.Value;
                                if (message != null || value?.Tag != "string" || value.Value == null) return Invalid();
                                message = value.Value;
                            }
                            else if (item.Data.Key == "timestamp")
                            {
                                var value = Decode<NumericMetadata>(line)?.Data?.Value;
                                if (timestamp != null || value?.Tag != "numeric" || value.Value == null) return Invalid();
                                timestamp = value.Value;
                            }
                            break;
                        case "complete":
                            if (!header || item.Data?.Status != 0 || !FinishPending()) return Invalid();
                            completed = true;
                            break;
                        case "log": break;
                        default: return Invalid();
                    }
                }
                return completed ? Result<IReadOnlyList<RevisionHistoryEntry>>.Success(result.AsReadOnly()) : Invalid();
            }
            catch (Exception e) when (e is SerializationException || e is ArgumentException ||
                                      e is InvalidCastException || e is OverflowException)
            { return Invalid(); }

            bool FinishPending()
            {
                if (revision == null) return true;
                if (message == null || timestamp == null) return false;
                var date = DateTimeOffset.FromUnixTimeMilliseconds(timestamp.Value).UtcDateTime;
                result.Add(new RevisionHistoryEntry(new RevisionSignature(revision),
                    new RevisionNumber(number.Value), message, date));
                revision = null;
                number = null;
                message = null;
                timestamp = null;
                return true;
            }
        }

        private static T Decode<T>(string line) where T : class
        {
            using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
            {
                var serializer = new DataContractJsonSerializer(typeof(T));
                var value = (T)serializer.ReadObject(stream);
                return stream.Position == stream.Length ? value : null;
            }
        }

        private static bool Hex(string value, int count)
        {
            if (value == null || value.Length != count) return false;
            foreach (var c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        private static Result<IReadOnlyList<RevisionHistoryEntry>> Invalid() =>
            Result<IReadOnlyList<RevisionHistoryEntry>>.Failure(new LoreError(ErrorCode.ValidationFailed,
                "Lore CLI returned an unsupported or incomplete history event stream."));
    }
}
