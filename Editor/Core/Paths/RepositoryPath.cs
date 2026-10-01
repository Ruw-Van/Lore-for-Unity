using System;

namespace Lore.Unity.Core.Paths
{
    public readonly struct RepositoryPath : IEquatable<RepositoryPath>
    {
        public RepositoryPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A repository path is required.", nameof(value));
            }

            Value = value.Replace('\\', '/');
            if (Value.StartsWith("/", StringComparison.Ordinal) ||
                (Value.Length >= 2 && Value[1] == ':') ||
                Value.IndexOf('\0') >= 0 || HasTraversal(Value))
                throw new ArgumentException("A repository-relative path without traversal is required.", nameof(value));
        }

        public string Value { get; }

        private static bool HasTraversal(string value)
        {
            foreach (var part in value.Split('/'))
                if (part == ".." || part == "." || part.Length == 0) return true;
            return false;
        }

        public bool Equals(RepositoryPath other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is RepositoryPath other && Equals(other);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);
        }

        public override string ToString()
        {
            return Value;
        }
    }
}
