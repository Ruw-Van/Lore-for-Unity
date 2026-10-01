using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Repository;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Status;

namespace Lore.Unity.Infrastructure.LoreCli
{
    // The hidden --json option in EpicGames/lore serializes LoreEvent as
    // {"tagName":"repositoryStatusFile","data":{...}} (one event per line).
    public sealed class CliStatusParser
    {
        [DataContract]
        private sealed class Event
        {
            [DataMember(Name = "tagName")] public string Tag { get; set; }
            [DataMember(Name = "data")] public EventData Data { get; set; }
        }

        [DataContract]
        private sealed class EventData
        {
            [DataMember(Name = "repository")] public string Repository { get; set; }
            [DataMember(Name = "branchName")] public string BranchName { get; set; }
            [DataMember(Name = "revision")] public string Revision { get; set; }
            [DataMember(Name = "remoteAvailable")] public byte? RemoteAvailable { get; set; }
            [DataMember(Name = "remoteAuthorized")] public byte? RemoteAuthorized { get; set; }
            [DataMember(Name = "isLocalAhead")] public byte? LocalAhead { get; set; }
            [DataMember(Name = "isRemoteAhead")] public byte? RemoteAhead { get; set; }
            [DataMember(Name = "path")] public string Path { get; set; }
            [DataMember(Name = "fromPath")] public string FromPath { get; set; }
            [DataMember(Name = "action")] public string Action { get; set; }
            [DataMember(Name = "flagStaged")] public bool? Staged { get; set; }
            [DataMember(Name = "flagDirty")] public bool? Dirty { get; set; }
            [DataMember(Name = "flagConflict")] public bool? Conflict { get; set; }
            [DataMember(Name = "flagConflictUnresolved")] public bool? Unresolved { get; set; }
            [DataMember(Name = "status")] public int? Status { get; set; }
        }

        public Result<CliStatusData> Parse(AbsolutePath root, string output)
        {
            if (string.IsNullOrEmpty(root.Value) || string.IsNullOrWhiteSpace(output)) return Invalid();
            EventData revision = null;
            var files = new List<EventData>();
            var complete = false;
            try
            {
                foreach (var line in output.TrimEnd('\r', '\n').Split('\n'))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.Length > 65536) return Invalid();
                    var serializer = new DataContractJsonSerializer(typeof(Event));
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                    {
                        var item = (Event)serializer.ReadObject(stream);
                        if (stream.Position != stream.Length || item?.Tag == null) return Invalid();
                        switch (item.Tag)
                        {
                            case "repositoryStatusRevision":
                                if (revision != null || complete || item.Data == null) return Invalid();
                                revision = item.Data;
                                break;
                            case "repositoryStatusFile":
                                if (revision == null || complete || item.Data == null) return Invalid();
                                files.Add(item.Data);
                                break;
                            case "complete":
                                if (complete || item.Data?.Status != 0) return Invalid();
                                complete = true;
                                break;
                            case "end":
                            case "log":
                            case "progress":
                            case "maintenance":
                            case "repositoryStatusSummary":
                            case "repositoryStatusCount":
                            case "pathIgnore":
                                break;
                            default: return Invalid();
                        }
                    }
                }
                if (!complete || revision == null || !Hex(revision.Repository, 32) ||
                    !Hex(revision.Revision, 64) || string.IsNullOrWhiteSpace(revision.BranchName)) return Invalid();
                var repository = new RepositorySnapshot(new RepositoryId(revision.Repository), root,
                    new BranchName(revision.BranchName), new RevisionSignature(revision.Revision));
                var remote = Remote(revision);
                var result = new List<FileStatusEntry>();
                var seen = new HashSet<RepositoryPath>();
                foreach (var file in files)
                {
                    if (file.Staged == null || file.Dirty == null || file.Conflict == null ||
                        file.Unresolved == null || file.Action == null) return Invalid();
                    var path = new RepositoryPath(file.Path);
                    if (!seen.Add(path)) return Invalid(); // staged + dirty for one path requires aggregation
                    WorkingState working;
                    RepositoryPath? from = null;
                    switch (file.Action)
                    {
                        case "keep": working = file.Dirty.Value ? WorkingState.Modified : WorkingState.Unchanged; break;
                        case "add": working = file.Staged.Value ? WorkingState.Added : WorkingState.Untracked; break;
                        case "delete": working = WorkingState.Deleted; break;
                        case "move":
                            working = WorkingState.Moved;
                            break;
                        case "copy": working = WorkingState.Copied; break;
                        default: return Invalid();
                    }
                    if (working == WorkingState.Moved || working == WorkingState.Copied)
                    {
                        if (string.IsNullOrWhiteSpace(file.FromPath)) return Invalid();
                        from = new RepositoryPath(file.FromPath);
                    }
                    var conflict = file.Conflict.Value ? (file.Unresolved.Value ? ConflictState.Conflicted : ConflictState.Resolved) : ConflictState.None;
                    result.Add(new FileStatusEntry(path, new FileStatus(working,
                        file.Staged.Value ? StageState.Staged : StageState.Unstaged,
                        LockState.Unknown, conflict, remote), from));
                }
                return Result<CliStatusData>.Success(new CliStatusData(repository, result.AsReadOnly()));
            }
            catch (Exception e) when (e is SerializationException || e is ArgumentException ||
                                      e is FormatException || e is InvalidCastException)
            {
                return Invalid();
            }
        }

        private static RemoteState Remote(EventData revision)
        {
            if (revision.RemoteAvailable != 1 || revision.RemoteAuthorized != 1 ||
                revision.LocalAhead == null || revision.RemoteAhead == null) return RemoteState.Unknown;
            if (revision.LocalAhead == 1) return revision.RemoteAhead == 1 ? RemoteState.Diverged : RemoteState.Ahead;
            return revision.RemoteAhead == 1 ? RemoteState.Behind : RemoteState.UpToDate;
        }

        private static bool Hex(string value, int length)
        {
            if (value == null || value.Length != length) return false;
            foreach (var c in value)
                if (!Uri.IsHexDigit(c)) return false;
            return true;
        }

        private static Result<CliStatusData> Invalid() =>
            Result<CliStatusData>.Failure(new LoreError(ErrorCode.ValidationFailed,
                "Lore CLI returned an unsupported or incomplete event stream."));
    }

    public sealed class CliStatusData
    {
        public CliStatusData(RepositorySnapshot repository, IReadOnlyList<FileStatusEntry> files)
        {
            Repository = repository;
            Files = files;
        }
        public RepositorySnapshot Repository { get; }
        public IReadOnlyList<FileStatusEntry> Files { get; }
    }
}
