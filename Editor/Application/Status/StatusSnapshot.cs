using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Lore.Unity.Application.Backend;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;

namespace Lore.Unity.Application.Status
{
    public sealed class StatusSnapshot
    {
        public StatusSnapshot(RepositoryId repository, long generation, DateTime refreshedUtc,
            IReadOnlyList<FileStatusEntry> entries)
        {
            Repository = repository ?? throw new ArgumentNullException(nameof(repository));
            if (generation < 0) throw new ArgumentOutOfRangeException(nameof(generation));
            if (refreshedUtc.Kind != DateTimeKind.Utc) throw new ArgumentException("UTC required.", nameof(refreshedUtc));
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var copy = new List<FileStatusEntry>(entries);
            var paths = new HashSet<RepositoryPath>();
            foreach (var entry in copy)
                if (entry == null || !paths.Add(entry.Path))
                    throw new ArgumentException("Null or duplicate status path.", nameof(entries));
            Repository = repository;
            Generation = generation;
            RefreshedUtc = refreshedUtc;
            Entries = new ReadOnlyCollection<FileStatusEntry>(copy);
        }

        public RepositoryId Repository { get; }
        public long Generation { get; }
        public DateTime RefreshedUtc { get; }
        public IReadOnlyList<FileStatusEntry> Entries { get; }
    }

    public sealed class StatusStore
    {
        private readonly object _gate = new object();
        private StatusSnapshot _current;
        private long _generation;

        public StatusSnapshot Current => System.Threading.Volatile.Read(ref _current);

        public long BeginRefresh()
        {
            lock (_gate) return ++_generation;
        }

        public bool TryPublish(StatusSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));
            // Lock closes the race between checking generation and replacing the snapshot.
            lock (_gate)
            {
                if (snapshot.Generation != _generation) return false;
                var previous = _current;
                if (previous != null && !previous.Repository.Equals(snapshot.Repository)) return false;
                System.Threading.Volatile.Write(ref _current, snapshot);
                return true;
            }
        }
    }
}
