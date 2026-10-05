using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Conflicts;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Infrastructure.LoreCli;

namespace Lore.Unity.Infrastructure.Recovery
{
    public sealed class FileConflictTextWorkspace : IConflictTextWorkspace
    {
        private const int MaxBytes = 8 * 1024 * 1024;
        private readonly RepositoryLocations _roots;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public FileConflictTextWorkspace(RepositoryLocations roots) =>
            _roots = roots ?? throw new ArgumentNullException(nameof(roots));

        public Task<Result<ConflictDraft>> ReadAsync(RepositoryId repository, RepositoryPath path,
            CancellationToken token) => Task.Run(() => Read(repository, path, token), token);

        public Task<Result> WriteIfUnchangedAsync(ConflictDraft draft, string result,
            CancellationToken token) => Task.Run(() => Write(draft, result, token), token);

        private Result<ConflictDraft> Read(RepositoryId repository, RepositoryPath path, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            if (repository == null || string.IsNullOrEmpty(path.Value) ||
                !_roots.TryGet(repository, out var root)) return Invalid<ConflictDraft>();
            try
            {
                var file = SafePath(root, path);
                var info = new FileInfo(file);
                if (!info.Exists || info.Length > MaxBytes) return Invalid<ConflictDraft>();
                var bytes = File.ReadAllBytes(file);
                if (bytes.Length > MaxBytes) return Invalid<ConflictDraft>();
                var bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
                var content = StrictUtf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                if (ConflictDocument.Parse(content).IsFailure) return Invalid<ConflictDraft>();
                return Result<ConflictDraft>.Success(new ConflictDraft(repository, path, content,
                    Hash(bytes)));
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                                      e is ArgumentException || e is DecoderFallbackException)
            { return Invalid<ConflictDraft>(); }
        }

        private Result Write(ConflictDraft draft, string result, CancellationToken token)
        {
            if (draft == null || result == null || result.IndexOf('\0') >= 0 ||
                ConflictDocument.ContainsMarkers(result) || !_roots.TryGet(draft.Repository, out var root))
                return Invalid();
            string temp = null;
            try
            {
                var file = SafePath(root, draft.Path);
                if (new FileInfo(file).Length > MaxBytes) return Invalid();
                var current = File.ReadAllBytes(file);
                if (Hash(current) != draft.Fingerprint) return Invalid();
                var bom = current.Length >= 3 && current[0] == 0xef && current[1] == 0xbb && current[2] == 0xbf;
                var encoded = StrictUtf8.GetBytes(result);
                if (encoded.Length + (bom ? 3 : 0) > MaxBytes) return Invalid();
                token.ThrowIfCancellationRequested();
                temp = Path.Combine(Path.GetDirectoryName(file), ".lore-resolution-" + Guid.NewGuid().ToString("N"));
                using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    if (bom) stream.Write(new byte[] { 0xef, 0xbb, 0xbf }, 0, 3);
                    stream.Write(encoded, 0, encoded.Length);
                    stream.Flush(true);
                }
                // Refuse a changed working copy or newly introduced link before replace.
                if (SafePath(root, draft.Path) != file || Hash(File.ReadAllBytes(file)) != draft.Fingerprint)
                    return Invalid();
                File.Replace(temp, file, null);
                temp = null;
                return Result.Success();
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                                      e is ArgumentException || e is EncoderFallbackException)
            { return Invalid(); }
            finally { if (temp != null) try { File.Delete(temp); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }

        private static string SafePath(AbsolutePath root, RepositoryPath path)
        {
            var basePath = Path.GetFullPath(root.Value).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (basePath.Length == 0) basePath = Path.DirectorySeparatorChar.ToString();
            var relative = path.Value.Replace('/', Path.DirectorySeparatorChar);
            var file = Path.GetFullPath(Path.Combine(basePath, relative));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            var prefix = basePath.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ?
                basePath : basePath + Path.DirectorySeparatorChar;
            if (!file.StartsWith(prefix, comparison))
                throw new ArgumentException("Path escapes repository.");
            var current = basePath;
            foreach (var part in relative.Split(Path.DirectorySeparatorChar))
            {
                current = Path.Combine(current, part);
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Links cannot be resolved automatically.");
            }
            return file;
        }

        private static string Hash(byte[] data)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(data)).Replace("-", string.Empty);
        }

        private static Result<T> Invalid<T>() => Result<T>.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Conflict file is unavailable, changed, linked, too large or not strict UTF-8 diff3 text."));
        private static Result Invalid() => Result.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Conflict file changed or cannot be replaced safely. Refresh Lore status before retry."));
    }
}
