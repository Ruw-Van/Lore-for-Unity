using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Infrastructure.LoreCli
{
    public sealed class CliDiffAdapter : IDiffBackend
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
            [DataMember(Name = "path")] public string Path { get; set; }
            [DataMember(Name = "patch")] public string Patch { get; set; }
            [DataMember(Name = "status")] public int? Status { get; set; }
        }

        private readonly LoreCliRunner _runner;
        private readonly RepositoryLocations _roots;
        private readonly IRepositoryOperationGate _gate;

        public CliDiffAdapter(LoreCliRunner runner, RepositoryLocations roots, IRepositoryOperationGate gate)
        {
            _runner = runner ?? throw new ArgumentNullException(nameof(runner));
            _roots = roots ?? throw new ArgumentNullException(nameof(roots));
            _gate = gate ?? throw new ArgumentNullException(nameof(gate));
        }

        public async Task<Result<string>> ReadTextAsync(RepositoryId repository, RepositoryPath path, CancellationToken token)
        {
            if (repository == null) throw new ArgumentNullException(nameof(repository));
            if (string.IsNullOrEmpty(path.Value)) throw new ArgumentException("Path required.", nameof(path));
            if (!_roots.TryGet(repository, out var root)) return Invalid();
            using (await _gate.AcquireAsync(repository, token))
            {
                var output = await _runner.RunAsync(root,
                    new[] { "--json", "--offline", "diff", "--", path.Value }, token);
                if (output.IsFailure) return Result<string>.Failure(output.Error);
                if (output.Value.ExitCode != 0) return Invalid();
                return Parse(path, output.Value.StandardOutput);
            }
        }

        public static Result<string> Parse(RepositoryPath path, string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return Invalid();
            string patch = null;
            var completed = false;
            try
            {
                foreach (var line in text.TrimEnd('\r', '\n').Split('\n'))
                {
                    if (line.Length > 65536 || string.IsNullOrWhiteSpace(line)) return Invalid();
                    using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(line)))
                    {
                        var item = (Event)new DataContractJsonSerializer(typeof(Event)).ReadObject(stream);
                        if (item?.Tag == null || stream.Position != stream.Length || completed) return Invalid();
                        switch (item.Tag)
                        {
                            case "fileDiff":
                                if (patch != null || item.Data?.Path != path.Value || item.Data.Patch == null)
                                    return Invalid();
                                patch = item.Data.Patch;
                                break;
                            case "complete":
                                if (item.Data?.Status != 0) return Invalid();
                                completed = true;
                                break;
                            case "log": break;
                            default: return Invalid();
                        }
                    }
                }
                return completed && patch != null ? Result<string>.Success(patch) : Invalid();
            }
            catch (Exception e) when (e is SerializationException || e is ArgumentException || e is InvalidCastException)
            { return Invalid(); }
        }

        private static Result<string> Invalid() => Result<string>.Failure(new LoreError(ErrorCode.ValidationFailed,
            "Lore CLI returned an unsupported, binary, or incomplete diff."));
    }
}
