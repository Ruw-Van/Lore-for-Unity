using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Conflicts;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.Recovery
{
    public sealed class ExternalMergeRunner : IExternalMergeExecutor
    {
        private const int MaxBytes = 8 * 1024 * 1024;
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        public Task<Result<string>> MergeAsync(ExternalMergeTool tool, ConflictDocument document,
            CancellationToken token) => Task.Run(() => Run(tool, document, token), token);

        private Result<string> Run(ExternalMergeTool tool, ConflictDocument document, CancellationToken token)
        {
            if (tool == null || document == null) throw new ArgumentNullException(tool == null ? nameof(tool) : nameof(document));
            token.ThrowIfCancellationRequested();
            if (!File.Exists(tool.Executable)) return Failed("Configured external merge executable does not exist.");
            var directory = Path.Combine(Path.GetTempPath(), "LoreForUnityMerge-" + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(directory);
                var basePath = Path.Combine(directory, "base.txt");
                var mine = Path.Combine(directory, "mine.txt");
                var theirs = Path.Combine(directory, "theirs.txt");
                var result = Path.Combine(directory, "result.txt");
                File.WriteAllText(basePath, document.Version(0), StrictUtf8);
                File.WriteAllText(mine, document.Version(1), StrictUtf8);
                File.WriteAllText(theirs, document.Version(2), StrictUtf8);
                var info = new ProcessStartInfo
                {
                    FileName = tool.Executable,
                    WorkingDirectory = directory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };
                var secretKeys = new List<string>();
                foreach (var pair in info.Environment)
                {
                    var key = pair.Key.ToUpperInvariant();
                    if (key.Contains("TOKEN") || key.Contains("SECRET") || key.Contains("PASSWORD") ||
                        key.Contains("CREDENTIAL") || key.Contains("AUTH") || key.EndsWith("_KEY", StringComparison.Ordinal))
                        secretKeys.Add(pair.Key);
                }
                foreach (var key in secretKeys) info.Environment.Remove(key);
                foreach (var argument in tool.Expand(basePath, mine, theirs, result))
                    info.ArgumentList.Add(argument);
                using (var process = new Process { StartInfo = info })
                {
                    if (!process.Start()) return Failed("External merge tool could not be started.");
                    // Drain without recording output; tools may print sensitive file contents.
                    var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
                    var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
                    using (token.Register(() => TryKill(process)))
                    {
                        if (!process.WaitForExit(10 * 60 * 1000))
                        {
                            TryKill(process);
                            process.WaitForExit();
                            return Failed("External merge tool timed out; no resolution was applied.");
                        }
                    }
                    if (token.IsCancellationRequested || process.ExitCode != 0)
                        return Failed("External merge tool did not complete successfully; no resolution was applied.");
                    Task.WhenAll(stdout, stderr).GetAwaiter().GetResult();
                }
                if (!File.Exists(result) || new FileInfo(result).Length > MaxBytes ||
                    (File.GetAttributes(result) & FileAttributes.ReparsePoint) != 0)
                    return Failed("External merge tool did not produce a safe result file.");
                var bytes = File.ReadAllBytes(result);
                if (bytes.Length > MaxBytes) return Failed("External merge result exceeded the size limit.");
                var bom = bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
                var content = StrictUtf8.GetString(bytes, bom ? 3 : 0, bytes.Length - (bom ? 3 : 0));
                if (content.IndexOf('\0') >= 0 || ConflictDocument.ContainsMarkers(content))
                    return Failed("External merge result still contains conflict markers or binary data.");
                return Result<string>.Success(content);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException ||
                                      e is Win32Exception || e is DecoderFallbackException || e is ArgumentException)
            { return Failed("External merge tool or temporary files could not be processed."); }
            finally
            {
                // Delete only known files, never recursively follow tool-created paths.
                foreach (var name in new[] { "base.txt", "mine.txt", "theirs.txt", "result.txt" })
                    try { File.Delete(Path.Combine(directory, name)); }
                    catch (IOException) { } catch (UnauthorizedAccessException) { }
                try { if (Directory.Exists(directory)) Directory.Delete(directory); }
                catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        private static void TryKill(Process process)
        {
            try { if (!process.HasExited) process.Kill(); }
            catch (InvalidOperationException) { } catch (Win32Exception) { }
        }

        private static Result<string> Failed(string message) => Result<string>.Failure(
            new LoreError(ErrorCode.ValidationFailed, message));
    }
}
