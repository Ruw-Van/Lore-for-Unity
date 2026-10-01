namespace Lore.Unity.Core.Identifiers
{
    public sealed class RepositoryId : StringIdentifier
    {
        public RepositoryId(string value) : base(value, nameof(value)) { }
    }

    public sealed class BranchId : StringIdentifier
    {
        public BranchId(string value) : base(value, nameof(value)) { }
    }

    public sealed class BranchName : StringIdentifier
    {
        public BranchName(string value) : base(value, nameof(value)) { }
    }

    public sealed class RevisionSignature : StringIdentifier
    {
        public RevisionSignature(string value) : base(value, nameof(value)) { }
    }

    public readonly struct RevisionNumber : System.IEquatable<RevisionNumber>
    {
        public RevisionNumber(long value)
        {
            if (value < 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(value));
            }

            Value = value;
        }

        public long Value { get; }

        public bool Equals(RevisionNumber other) => Value == other.Value;

        public override bool Equals(object obj) => obj is RevisionNumber other && Equals(other);

        public override int GetHashCode() => Value.GetHashCode();

        public override string ToString()
        {
            return Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
    }

    public sealed class UnityAssetGuid : StringIdentifier
    {
        public UnityAssetGuid(string value) : base(value, nameof(value)) { }
    }

    public sealed class LoreVersion : StringIdentifier
    {
        public LoreVersion(string value) : base(value, nameof(value)) { }
    }

    public sealed class OperationId : StringIdentifier
    {
        public OperationId(string value) : base(value, nameof(value)) { }
    }
}
