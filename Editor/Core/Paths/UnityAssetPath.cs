using System;

namespace Lore.Unity.Core.Paths
{
    public readonly struct UnityAssetPath : IEquatable<UnityAssetPath>
    {
        public UnityAssetPath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.IndexOf('\\') >= 0)
            {
                throw new ArgumentException("A Unity asset path using '/' separators is required.", nameof(value));
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(UnityAssetPath other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is UnityAssetPath other && Equals(other);
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
