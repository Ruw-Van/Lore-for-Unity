using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.LoreCli
{
    public sealed class CliOutput
    {
        public CliOutput(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput;
            StandardError = standardError;
        }
        public int ExitCode { get; }
        public string StandardOutput { get; }
        public string StandardError { get; }
    }

    // Transport only. No CLI output is interpreted as a Lore domain result here.
    public sealed class LoreCliRunner
    {
        private readonly AbsolutePath _executable;
        private const int MaxChars = 1024 * 1024;

        public LoreCliRunner(AbsolutePath verifiedExecutable)
        {
            if (string.IsNullOrEmpty(verifiedExecutable.Value)) throw new ArgumentException("Executable required.", nameof(verifiedExecutable));
            _executable = verifiedExecutable;
        }

        public Task<Result<CliOutput>> RunAsync(AbsolutePath repositoryRoot,
            IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(repositoryRoot.Value)) throw new ArgumentException("Repository root required.", nameof(repositoryRoot));
            if (arguments == null) throw new ArgumentNullException(nameof(arguments));
            return Task.Run(() => RunCoreAsync(repositoryRoot, arguments, cancellationToken), cancellationToken);
        }

        private async Task<Result<CliOutput>> RunCoreAsync(AbsolutePath repositoryRoot,
            IReadOnlyList<string> arguments, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!File.Exists(_executable.Value)) return Failure(ErrorCode.RuntimeMissing, "Configured Lore CLI is missing.");
            var start = new ProcessStartInfo
            {
                FileName = _executable.Value,
                WorkingDirectory = repositoryRoot.Value,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("--repository");
            start.ArgumentList.Add(repositoryRoot.Value);
            start.ArgumentList.Add("--non-interactive");
            start.ArgumentList.Add("--no-pager");
            foreach (var arg in arguments)
            {
                if (arg == null || arg.IndexOf('\0') >= 0) throw new ArgumentException("Invalid argument.", nameof(arguments));
                start.ArgumentList.Add(arg);
            }
            try
            {
                using (var process = new Process { StartInfo = start })
                {
                    if (!process.Start()) return Failure(ErrorCode.RuntimeMissing, "Lore CLI did not start.");
                    using (cancellationToken.Register(() => { try { if (!process.HasExited) process.Kill(); }
                        catch (InvalidOperationException) { } catch (Win32Exception) { } }))
                    {
                        var stdout = ReadBoundedAsync(process.StandardOutput);
                        var stderr = ReadBoundedAsync(process.StandardError);
                        await Task.Run(() => process.WaitForExit());
                        var output = await stdout;
                        var error = await stderr;
                        cancellationToken.ThrowIfCancellationRequested();
                        if (output == null || error == null)
                            return Failure(ErrorCode.ValidationFailed, "Lore CLI output exceeded the limit.");
                        return Result<CliOutput>.Success(new CliOutput(process.ExitCode, output, error));
                    }
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Win32Exception) { return Failure(ErrorCode.RuntimeMissing, "Lore CLI cannot be started."); }
            catch (IOException) { return Failure(ErrorCode.Unknown, "Lore CLI I/O failed."); }
        }

        private static async Task<string> ReadBoundedAsync(StreamReader reader)
        {
            var output = new StringBuilder();
            var buffer = new char[4096];
            var overflow = false;
            int count;
            while ((count = await reader.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                if (output.Length + count > MaxChars) overflow = true;
                if (!overflow) output.Append(buffer, 0, count);
            }
            return overflow ? null : output.ToString();
        }

        private static Result<CliOutput> Failure(ErrorCode code, string message) =>
            Result<CliOutput>.Failure(new LoreError(code, message));
    }
}
