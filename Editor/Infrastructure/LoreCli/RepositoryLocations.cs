using System;
using System.Collections.Concurrent;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;

namespace Lore.Unity.Infrastructure.LoreCli
{
    public sealed class RepositoryLocations
    {
        private readonly ConcurrentDictionary<RepositoryId, AbsolutePath> _roots =
            new ConcurrentDictionary<RepositoryId, AbsolutePath>();

        public void Record(RepositoryId id, AbsolutePath root)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            if (string.IsNullOrEmpty(root.Value)) throw new ArgumentException("Root required.", nameof(root));
            _roots[id] = root;
        }

        public bool TryGet(RepositoryId id, out AbsolutePath root)
        {
            if (id == null) throw new ArgumentNullException(nameof(id));
            return _roots.TryGetValue(id, out root);
        }
    }
}
