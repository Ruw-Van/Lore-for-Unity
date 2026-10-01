using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Repository;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Status;

namespace Lore.Unity.Infrastructure.LoreCli
{
    // Restricted parser for the documented CLI status display. Unknown sections and
    // ambiguous staged/move/conflict entries are rejected, never silently dropped.
    public sealed class CliStatusParser
    {
        private static readonly Regex Identity = new Regex(@"^Repository ([a-fA-F0-9]{32})$", RegexOptions.CultureInvariant);
        private static readonly Regex Revision = new Regex(@"^On branch (\S+) revision (\d+) -> ([a-fA-F0-9]{64})$", RegexOptions.CultureInvariant);

        public Result<RepositorySnapshot> ParseRepository(AbsolutePath root, string text)
        {
            var lines = Lines(text);
            if (lines == null || lines.Length < 2) return InvalidRepository();
            var id = Identity.Match(lines[0]);
            var revision = Revision.Match(lines[1]);
            if (!id.Success || !revision.Success) return InvalidRepository();
            return Result<RepositorySnapshot>.Success(new RepositorySnapshot(new RepositoryId(id.Groups[1].Value), root,
                new BranchName(revision.Groups[1].Value), new RevisionSignature(revision.Groups[3].Value)));
        }

        public Result<IReadOnlyList<FileStatusEntry>> ParseFiles(string text)
        {
            var lines = Lines(text);
            if (lines == null || lines.Length < 2 || !Identity.IsMatch(lines[0]) || !Revision.IsMatch(lines[1]))
                return InvalidFiles();
            var entries = new List<FileStatusEntry>();
            var paths = new HashSet<RepositoryPath>();
            var section = "";
            for (var i = 2; i < lines.Length; i++)
            {
                var line = lines[i];
                if (line.StartsWith("Remote revision ", StringComparison.Ordinal) ||
                    line == "Local branch in sync with remote" ||
                    line == "Local branch is ahead of remote" ||
                    line == "Local branch is behind remote" ||
                    line == "Remote branch does not exist") continue;
                if (line == "Changes not staged for commit:" || line == "Untracked files:")
                {
                    section = line;
                    continue;
                }
                // The CLI emits additional metadata for staged, moved and conflicted files.
                // Their human-readable syntax is not a lossless machine protocol.
                if (line.Length < 3 || line[1] != ' ' || section.Length == 0) return InvalidFiles();
                var action = line[0];
                WorkingState working;
                if (action == 'M') working = WorkingState.Modified;
                else if (action == 'D') working = WorkingState.Deleted;
                else if (action == 'A' && section == "Untracked files:") working = WorkingState.Untracked;
                else return InvalidFiles();
                try
                {
                    var path = new RepositoryPath(line.Substring(2));
                    if (!paths.Add(path)) return InvalidFiles();
                    entries.Add(new FileStatusEntry(path, new FileStatus(working, StageState.Unstaged,
                        LockState.Unknown, ConflictState.Unknown, RemoteState.Unknown)));
                }
                catch (ArgumentException) { return InvalidFiles(); }
            }
            return Result<IReadOnlyList<FileStatusEntry>>.Success(entries.AsReadOnly());
        }

        private static string[] Lines(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf('\u001b') >= 0 || text.IndexOf('\0') >= 0) return null;
            return text.TrimEnd('\r', '\n').Replace("\r\n", "\n").Split('\n');
        }

        private static Result<RepositorySnapshot> InvalidRepository() =>
            Result<RepositorySnapshot>.Failure(new LoreError(ErrorCode.InvalidRepository, "Unknown CLI repository output."));
        private static Result<IReadOnlyList<FileStatusEntry>> InvalidFiles() =>
            Result<IReadOnlyList<FileStatusEntry>>.Failure(new LoreError(ErrorCode.UnsupportedOperation,
                "CLI status output cannot be mapped without losing information."));
    }
}
