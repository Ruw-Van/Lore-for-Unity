using System;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;

namespace Lore.Unity.Core.Repository
{
    public sealed class RepositorySnapshot
    {
        public RepositorySnapshot(RepositoryId id, AbsolutePath root, BranchName branch,
            RevisionSignature revision)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrEmpty(root.Value)) throw new ArgumentException("A root is required.", nameof(root));
            Root = root;
            Branch = branch;
            Revision = revision;
        }

        public RepositoryId Id { get; }
        public AbsolutePath Root { get; }
        public BranchName Branch { get; }
        public RevisionSignature Revision { get; }
    }
}
