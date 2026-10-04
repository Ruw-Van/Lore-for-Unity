using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.Recovery
{
    public sealed class PendingRecovery
    {
        public PendingRecovery(OperationId id, RepositoryId repository, string operation, bool loreApplied)
        {
            Id = id;
            Repository = repository;
            Operation = operation;
            LoreApplied = loreApplied;
        }
        public OperationId Id { get; }
        public RepositoryId Repository { get; }
        public string Operation { get; }
        // False means unknown: the process may have stopped between Lore's write and the marker.
        public bool LoreApplied { get; }
    }

    // Append-only boundary markers. A completed operation remains on disk so a
    // partial cleanup can never make it look pending again. Files belong under
    // the host project's Library (not the package or Lore repository).
    public sealed class FileRecoveryJournal : IRecoveryJournal
    {
        private readonly string _directory;
        private readonly object _mutex = new object();

        public FileRecoveryJournal(AbsolutePath directory)
        {
            if (string.IsNullOrEmpty(directory.Value)) throw new ArgumentException("Journal directory required.", nameof(directory));
            _directory = directory.Value;
        }

        public Task<Result> BeginAsync(OperationId id, RepositoryId repository, string operation, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (operation != "Sync" && operation != "BranchSwitch") throw new ArgumentException("Unknown operation.", nameof(operation));
            var name = Name(id);
            lock (_mutex)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (ProcessLock())
                    {
                        var pending = PendingCore();
                        if (pending.IsFailure) return Task.FromResult(Result.Failure(pending.Error));
                        foreach (var item in pending.Value)
                            if (item.Repository.Equals(repository))
                                return Task.FromResult(Failure("An unresolved write must be inspected before another write."));
                        return Task.FromResult(Write(name + ".begin", repository.Value + "\n" + operation + "\n"));
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                { return Task.FromResult(Failure("Recovery journal is unavailable or in use by another Editor.")); }
            }
        }

        public Task<Result> LoreAppliedAsync(OperationId id, CancellationToken token) =>
            Mark(id, ".applied", ".begin", token);

        public Task<Result> CompleteAsync(OperationId id, CancellationToken token) =>
            Mark(id, ".complete", ".applied", token);

        public Result<IReadOnlyList<PendingRecovery>> Pending()
        {
            lock (_mutex)
            {
                try { using (ProcessLock()) return PendingCore(); }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                { return Invalid<IReadOnlyList<PendingRecovery>>(); }
            }
        }

        private Task<Result> Mark(OperationId id, string suffix, string prerequisite, CancellationToken token)
        {
            var name = Name(id);
            lock (_mutex)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    using (ProcessLock())
                    {
                        if (!File.Exists(Path.Combine(_directory, name + prerequisite)))
                            return Task.FromResult(Failure("Journal boundary is missing."));
                        return Task.FromResult(Write(name + suffix, string.Empty));
                    }
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
                { return Task.FromResult(Failure("Recovery journal is unavailable or in use by another Editor.")); }
            }
        }

        private FileStream ProcessLock()
        {
            Directory.CreateDirectory(_directory);
            return new FileStream(Path.Combine(_directory, ".journal.lock"), FileMode.OpenOrCreate,
                FileAccess.ReadWrite, FileShare.None);
        }

        private Result<IReadOnlyList<PendingRecovery>> PendingCore()
        {
            var items = new List<PendingRecovery>();
            try
            {
                if (!Directory.Exists(_directory)) return Result<IReadOnlyList<PendingRecovery>>.Success(items.AsReadOnly());
                foreach (var file in Directory.GetFiles(_directory))
                {
                    var filename = Path.GetFileName(file);
                    if (filename == ".journal.lock") continue;
                    if (!filename.EndsWith(".begin", StringComparison.Ordinal))
                    {
                        if (filename.EndsWith(".applied", StringComparison.Ordinal) ||
                            filename.EndsWith(".complete", StringComparison.Ordinal))
                        {
                            var stem = filename.Substring(0, filename.LastIndexOf('.'));
                            if (!ValidName(stem) || !File.Exists(Path.Combine(_directory, stem + ".begin")))
                                return Invalid<IReadOnlyList<PendingRecovery>>();
                        }
                        else return Invalid<IReadOnlyList<PendingRecovery>>();
                        continue;
                    }
                    var name = filename.Substring(0, filename.Length - ".begin".Length);
                    if (!ValidName(name)) return Invalid<IReadOnlyList<PendingRecovery>>();
                    var lines = File.ReadAllLines(file, Encoding.UTF8);
                    if (lines.Length != 2 || lines[0].Length != 32 || !Hex(lines[0]) ||
                        (lines[1] != "Sync" && lines[1] != "BranchSwitch"))
                        return Invalid<IReadOnlyList<PendingRecovery>>();
                    var applied = File.Exists(Path.Combine(_directory, name + ".applied"));
                    if (File.Exists(Path.Combine(_directory, name + ".complete")))
                    {
                        if (!applied) return Invalid<IReadOnlyList<PendingRecovery>>();
                        continue;
                    }
                    items.Add(new PendingRecovery(new OperationId(name), new RepositoryId(lines[0]),
                        lines[1], applied));
                }
                return Result<IReadOnlyList<PendingRecovery>>.Success(items.AsReadOnly());
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is ArgumentException)
            { return Invalid<IReadOnlyList<PendingRecovery>>(); }
        }

        private Result Write(string filename, string content)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                using (var stream = new FileStream(Path.Combine(_directory, filename), FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    var bytes = Encoding.UTF8.GetBytes(content);
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush(true);
                }
                return Result.Success();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            { return Failure("Recovery journal could not be persisted; inspect the journal before retry."); }
        }

        private static string Name(OperationId id)
        {
            if (id == null || !ValidName(id.Value)) throw new ArgumentException("Invalid operation ID.", nameof(id));
            return id.Value;
        }

        private static bool ValidName(string value) => value != null && value.Length == 32 && Hex(value);
        private static bool Hex(string value)
        {
            foreach (var c in value) if (!Uri.IsHexDigit(c) || char.IsUpper(c)) return false;
            return true;
        }

        private static Result Failure(string message) => Result.Failure(new LoreError(ErrorCode.ValidationFailed, message));
        private static Result<T> Invalid<T>() => Result<T>.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Recovery journal is incomplete or corrupt; writes are blocked."));
    }
}
