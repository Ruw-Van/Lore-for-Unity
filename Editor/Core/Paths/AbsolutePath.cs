using System;
using System.IO;

namespace Lore.Unity.Core.Paths
{
    public readonly struct AbsolutePath : IEquatable<AbsolutePath>
    {
        public AbsolutePath(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Path.IsPathRooted(value))
            {
                throw new ArgumentException("An absolute path is required.", nameof(value));
            }

            Value = Path.GetFullPath(value);
        }

        public string Value { get; }

        public bool Equals(AbsolutePath other)
        {
            return string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is AbsolutePath other && Equals(other);
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
