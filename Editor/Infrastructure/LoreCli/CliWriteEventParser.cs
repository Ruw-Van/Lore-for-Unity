using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.LoreCli
{
    public sealed class CliWriteEventParser
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
            [DataMember(Name = "status")] public int? Status { get; set; }
            [DataMember(Name = "revision")] public string Revision { get; set; }
            [DataMember(Name = "repository")] public string Repository { get; set; }
            [DataMember(Name = "name")] public string Name { get; set; }
            [DataMember(Name = "targetRevision")] public string TargetRevision { get; set; }
        }
        [DataContract]
        private sealed class BranchEvent
        {
            [DataMember(Name = "data")] public BranchData Data { get; set; }
        }
        [DataContract]
        private sealed class BranchData
        {
            [DataMember(Name = "branch")] public BranchInfo Branch { get; set; }
        }
        [DataContract]
        private sealed class BranchInfo
        {
            [DataMember(Name = "name")] public string Name { get; set; }
        }

        public Result Verify(string text)
        {
            var events = Read(text);
            if (events.IsFailure) return Result.Failure(events.Error);
            return Complete(events.Value);
        }

        public Result<RevisionSignature> Commit(RepositoryId repository, string text)
        {
            var events = Read(text);
            if (events.IsFailure) return Result<RevisionSignature>.Failure(events.Error);
            var complete = Complete(events.Value);
            if (complete.IsFailure) return Result<RevisionSignature>.Failure(complete.Error);
            string signature = null;
            foreach (var item in events.Value)
                if (item.Tag == "revisionCommitRevision")
                {
                    if (signature != null || item.Data?.Repository != repository.Value ||
                        !Hex(item.Data.Revision, 64)) return Invalid<RevisionSignature>();
                    signature = item.Data.Revision;
                }
            return signature == null ? Invalid<RevisionSignature>() :
                Result<RevisionSignature>.Success(new RevisionSignature(signature));
        }

        // The CLI may create the local revision, emit complete:0, then fail
        // relaying to a remote. This is only a candidate until a fresh Lore
        // status confirms that the repository HEAD equals this signature.
        public Result<RevisionSignature> LocalCommitCandidate(RepositoryId repository, string text)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            var events = Read(text);
            if (events.IsFailure) return Result<RevisionSignature>.Failure(events.Error);
            string signature = null;
            var localCompleted = false;
            var remoteFailed = false;
            foreach (var item in events.Value)
            {
                if (item.Tag == "revisionCommitRevision")
                {
                    if (localCompleted || signature != null || item.Data?.Repository != repository.Value ||
                        !Hex(item.Data.Revision, 64)) return Invalid<RevisionSignature>();
                    signature = item.Data.Revision;
                }
                else if (item.Tag == "complete")
                {
                    if (!localCompleted && signature != null && item.Data?.Status == 0)
                        localCompleted = true;
                    else if (localCompleted && !remoteFailed && item.Data?.Status > 0)
                        remoteFailed = true;
                    else return Invalid<RevisionSignature>();
                }
            }
            return localCompleted && remoteFailed && events.Value[events.Value.Count - 1].Tag == "complete"
                ? Result<RevisionSignature>.Success(new RevisionSignature(signature)) : Invalid<RevisionSignature>();
        }

        public Result Sync(RepositoryId repository, string text)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            var events = Read(text);
            if (events.IsFailure) return Result.Failure(events.Error);
            var complete = Complete(events.Value);
            if (complete.IsFailure) return complete;
            var count = 0;
            foreach (var item in events.Value)
                if (item.Tag == "revisionSyncTarget")
                {
                    if (++count != 1 || item.Data?.Repository != repository.Value ||
                        !Hex(item.Data.TargetRevision, 64))
                        return Invalid();
                }
            return count == 1 ? Result.Success() : Invalid();
        }

        public Result BranchSwitch(BranchName branch, string text)
        {
            if (branch == null) throw new ArgumentNullException(nameof(branch));
            var events = Read(text);
            if (events.IsFailure) return Result.Failure(events.Error);
            var complete = Complete(events.Value);
            if (complete.IsFailure) return complete;
            var begin = 0;
            var end = 0;
            foreach (var item in events.Value)
            {
                if (item.Tag == "branchSwitchBegin") begin++;
                if (item.Tag == "branchSwitchEnd")
                    end++;
            }
            if (begin != 1 || end != 1) return Invalid();
            try
            {
                foreach (var line in text.TrimEnd('\r', '\n').Split('\n'))
                    if (line.Contains("\"tagName\":\"branchSwitchEnd\""))
                    {
                        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                        {
                            var serializer = new DataContractJsonSerializer(typeof(BranchEvent));
                            var item = (BranchEvent)serializer.ReadObject(stream);
                            if (item?.Data?.Branch?.Name != branch.Value) return Invalid();
                        }
                    }
            }
            catch (SerializationException) { return Invalid(); }
            return Result.Success();
        }

        public Result<IReadOnlyList<BranchName>> Branches(string text)
        {
            var events = Read(text);
            if (events.IsFailure) return Result<IReadOnlyList<BranchName>>.Failure(events.Error);
            var complete = Complete(events.Value);
            if (complete.IsFailure) return Result<IReadOnlyList<BranchName>>.Failure(complete.Error);
            var branches = new List<BranchName>();
            try
            {
                foreach (var item in events.Value)
                    if (item.Tag == "branchListEntry") branches.Add(new BranchName(item.Data?.Name));
            }
            catch (ArgumentException) { return Invalid<IReadOnlyList<BranchName>>(); }
            return Result<IReadOnlyList<BranchName>>.Success(branches.AsReadOnly());
        }

        private static Result Complete(IReadOnlyList<Event> events)
        {
            var count = 0;
            for (var index = 0; index < events.Count; index++)
            {
                var item = events[index];
                if (item.Tag == "complete")
                {
                    count++;
                    if (index != events.Count - 1 || item.Data?.Status == null)
                        return Result.Failure(new LoreError(ErrorCode.ValidationFailed,
                            "Lore CLI returned an incomplete result."));
                    if (item.Data.Status != 0)
                        return Result.Failure(new LoreError(ErrorCode.Unknown, "Lore CLI operation failed; re-query before retry."));
                }
            }
            return count == 1 ? Result.Success() : Result.Failure(new LoreError(ErrorCode.ValidationFailed,
                "Lore CLI returned an incomplete result."));
        }

        private static Result<IReadOnlyList<Event>> Read(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Invalid<IReadOnlyList<Event>>();
            var events = new List<Event>();
            try
            {
                foreach (var line in text.TrimEnd('\r', '\n').Split('\n'))
                {
                    if (line.Length > 65536 || string.IsNullOrWhiteSpace(line)) return Invalid<IReadOnlyList<Event>>();
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                    {
                        var serializer = new DataContractJsonSerializer(typeof(Event));
                        var item = (Event)serializer.ReadObject(stream);
                        if (item?.Tag == null || stream.Position != stream.Length) return Invalid<IReadOnlyList<Event>>();
                        events.Add(item);
                    }
                }
            }
            catch (Exception e) when (e is SerializationException || e is InvalidCastException || e is ArgumentException)
            { return Invalid<IReadOnlyList<Event>>(); }
            return Result<IReadOnlyList<Event>>.Success(events.AsReadOnly());
        }

        private static bool Hex(string value, int count)
        {
            if (value == null || value.Length != count) return false;
            foreach (var c in value) if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        private static Result<T> Invalid<T>() => Result<T>.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Lore CLI returned an invalid write event stream."));
        private static Result Invalid() => Result.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Lore CLI returned an invalid write event stream."));
    }
}
