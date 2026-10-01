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
        }

        public string Value { get; }

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
