using System;

namespace Lore.Unity.Core.Identifiers
{
    public abstract class StringIdentifier : IEquatable<StringIdentifier>
    {
        protected StringIdentifier(string value, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new ArgumentException("A non-empty identifier is required.", parameterName);
            }

            Value = value;
        }

        public string Value { get; }

        public bool Equals(StringIdentifier other)
        {
            return other != null &&
                   GetType() == other.GetType() &&
                   string.Equals(Value, other.Value, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as StringIdentifier);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (GetType().GetHashCode() * 397) ^ StringComparer.Ordinal.GetHashCode(Value);
            }
        }

        public override string ToString()
        {
            return Value;
        }
    }
}
