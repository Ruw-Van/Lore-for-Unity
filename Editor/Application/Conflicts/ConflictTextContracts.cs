using System;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;

namespace Lore.Unity.Application.Conflicts
{
    public sealed class ConflictDraft
    {
        public ConflictDraft(RepositoryId repository, RepositoryPath path, string original,
            string fingerprint)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            Path = path;
            Original = original ?? throw new ArgumentNullException(nameof(original));
            Fingerprint = fingerprint ?? throw new ArgumentNullException(nameof(fingerprint));
        }
        public RepositoryId Repository { get; }
        public RepositoryPath Path { get; }
        public string Original { get; }
        public string Fingerprint { get; }
    }

    public interface IConflictTextWorkspace
    {
        Task<Result<ConflictDraft>> ReadAsync(RepositoryId repository, RepositoryPath path, CancellationToken token);
        Task<Result> WriteIfUnchangedAsync(ConflictDraft draft, string result, CancellationToken token);
    }

    public interface IExternalMergeExecutor
    {
        Task<Result<string>> MergeAsync(ExternalMergeTool tool, ConflictDocument document,
            CancellationToken token);
    }

    public interface IEditedConflictBackend
    {
        Task<Result> StageAndResolveAsync(RepositoryId repository, RepositoryPath path, CancellationToken token);
    }

    public enum TextResolutionMode { Automatic, Manual, External }

    public sealed class TextResolutionRequest
    {
        public TextResolutionRequest(TextResolutionMode mode, ConflictDraft draft = null,
            string editedText = null, ExternalMergeTool tool = null)
        {
            Mode = mode;
            Draft = draft;
            EditedText = editedText;
            Tool = tool;
        }
        public TextResolutionMode Mode { get; }
        public ConflictDraft Draft { get; }
        public string EditedText { get; }
        public ExternalMergeTool Tool { get; }
    }
}
