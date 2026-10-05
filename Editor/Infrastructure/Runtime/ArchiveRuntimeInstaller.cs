using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.Runtime
{
    public sealed class ArchiveRuntimeInstaller : IRuntimeInstaller
    {
        private const long MaxEntryBytes = 128L * 1024 * 1024;
        private const long MaxTotalBytes = 256L * 1024 * 1024;
        private readonly IRuntimeProbe _probe;

        public ArchiveRuntimeInstaller(IRuntimeProbe probe) =>
            _probe = probe ?? throw new ArgumentNullException(nameof(probe));

        public async Task<Result> InstallAsync(AbsolutePath verifiedArtifact, AbsolutePath destination,
            ValidatedRuntimeArtifact artifact, LoreVersion version, CancellationToken token)
        {
            if (artifact == null || version == null) throw new ArgumentNullException(
                artifact == null ? nameof(artifact) : nameof(version));
            var parent = Path.GetDirectoryName(destination.Value);
            if (string.IsNullOrEmpty(parent)) return Invalid();
            var stage = Path.Combine(parent, ".install-" + Guid.NewGuid().ToString("N"));
            try
            {
                if (Directory.Exists(destination.Value)) return Invalid();
                Directory.CreateDirectory(parent);
                Directory.CreateDirectory(stage);
                var extracted = await Task.Run(() => Extract(verifiedArtifact.Value, stage, artifact, token), token);
                if (extracted.IsFailure) return extracted;
                var installed = await _probe.VerifyInstalledAsync(new AbsolutePath(stage), version,
                    artifact.Platform, token);
                if (installed.IsFailure) return installed;
                token.ThrowIfCancellationRequested();
                Directory.Move(stage, destination.Value);
                return Result.Success();
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is InvalidDataException)
            { return Invalid(); }
            finally { Cleanup(stage); }
        }

        private static Result Extract(string file, string stage, ValidatedRuntimeArtifact artifact,
            CancellationToken token)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            var executable = artifact.Platform == "Windows-x64" ? "lore.exe" : "lore";
            long total = 0;
            using (var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (artifact.ArtifactFormat == "zip" && artifact.Platform == "Windows-x64")
                {
                    using (var archive = new ZipArchive(input, ZipArchiveMode.Read, true))
                        foreach (var entry in archive.Entries)
                        {
                            token.ThrowIfCancellationRequested();
                            var mode = (entry.ExternalAttributes >> 16) & 0xF000;
                            if (mode == 0xA000 || !Accept(entry.FullName, names) ||
                                entry.Length > MaxEntryBytes || entry.Length < 0) return Invalid();
                            total += entry.Length;
                            if (total > MaxTotalBytes) return Invalid();
                            using (var source = entry.Open())
                                if (!CopyBounded(source, Path.Combine(stage, entry.FullName),
                                    entry.Length, token)) return Invalid();
                        }
                }
                else if (artifact.ArtifactFormat == "tar.gz" && artifact.Platform == "macOS-arm64")
                {
                    using (var gzip = new GZipStream(input, CompressionMode.Decompress, true))
                        if (!ExtractTar(gzip, stage, names, ref total, token)) return Invalid();
                }
                else return Invalid();
            }
            if (!names.Contains(executable)) return Invalid();
            if (artifact.Platform == "macOS-arm64" && RuntimeInformation.IsOSPlatform(OSPlatform.OSX) &&
                Chmod(Path.Combine(stage, executable), 0x1ed) != 0)
                return Invalid();
            return Result.Success();
        }

        private static bool ExtractTar(Stream archive, string stage, HashSet<string> names,
            ref long total, CancellationToken token)
        {
            var header = new byte[512];
            while (ReadExact(archive, header, header.Length))
            {
                token.ThrowIfCancellationRequested();
                var zero = true;
                foreach (var value in header) if (value != 0) { zero = false; break; }
                if (zero) return names.Count > 0;
                if (!TarChecksum(header, out var size) || size < 0 || size > MaxEntryBytes ||
                    header[156] != (byte)'0' && header[156] != 0) return false;
                var name = TarString(header, 0, 100);
                for (var i = 345; i < 500; i++) if (header[i] != 0) return false;
                if (!Accept(name, names)) return false;
                total += size;
                if (total > MaxTotalBytes || !CopyBounded(archive, Path.Combine(stage, name), size, token))
                    return false;
                var padding = (512 - (size % 512)) % 512;
                if (!Skip(archive, padding)) return false;
            }
            return false; // tar terminator must be present
        }

        private static bool Accept(string name, HashSet<string> names) =>
            (name == "lore.exe" || name == "lore" || name == "LICENSE.txt" ||
             name == "THIRD-PARTY-NOTICES.txt") && names.Add(name);

        private static bool CopyBounded(Stream source, string path, long length, CancellationToken token)
        {
            using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 81920, FileOptions.WriteThrough))
            {
                var buffer = new byte[81920];
                var remaining = length;
                while (remaining > 0)
                {
                    token.ThrowIfCancellationRequested();
                    var read = source.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                    if (read == 0) return false;
                    output.Write(buffer, 0, read);
                    remaining -= read;
                }
                output.Flush(true);
            }
            return true;
        }

        private static bool ReadExact(Stream source, byte[] buffer, int length)
        {
            var read = 0;
            while (read < length)
            {
                var count = source.Read(buffer, read, length - read);
                if (count == 0) return false;
                read += count;
            }
            return true;
        }

        private static bool Skip(Stream source, long length)
        {
            var buffer = new byte[512];
            while (length > 0)
            {
                var count = source.Read(buffer, 0, (int)Math.Min(length, buffer.Length));
                if (count == 0) return false;
                length -= count;
            }
            return true;
        }

        private static string TarString(byte[] data, int offset, int length)
        {
            var end = offset;
            while (end < offset + length && data[end] != 0) end++;
            for (var i = offset; i < end; i++) if (data[i] < 32 || data[i] > 126) return string.Empty;
            return System.Text.Encoding.ASCII.GetString(data, offset, end - offset);
        }

        private static bool TarChecksum(byte[] header, out long size)
        {
            size = -1;
            if (!Octal(header, 148, 8, out var expected) || !Octal(header, 124, 12, out size)) return false;
            long sum = 0;
            for (var i = 0; i < header.Length; i++) sum += i >= 148 && i < 156 ? 32 : header[i];
            return sum == expected;
        }

        private static bool Octal(byte[] data, int offset, int length, out long value)
        {
            value = 0;
            var started = false;
            for (var i = offset; i < offset + length; i++)
            {
                var c = data[i];
                if (c == 0 || c == 32)
                {
                    if (started) break;
                    continue;
                }
                if (c < (byte)'0' || c > (byte)'7') return false;
                started = true;
                value = checked(value * 8 + c - (byte)'0');
            }
            return started;
        }

        private static void Cleanup(string stage)
        {
            if (!Directory.Exists(stage)) return;
            foreach (var name in new[] { "lore.exe", "lore", "LICENSE.txt", "THIRD-PARTY-NOTICES.txt" })
                try { File.Delete(Path.Combine(stage, name)); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            try { Directory.Delete(stage); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }

        [DllImport("libc", EntryPoint = "chmod", SetLastError = true)]
        private static extern int Chmod(string path, int mode);

        private static Result Invalid() => Result.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Lore archive is unsafe, incomplete or unsupported; runtime was not published."));
    }
}
