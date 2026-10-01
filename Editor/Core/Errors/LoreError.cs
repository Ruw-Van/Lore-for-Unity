using System;

namespace Lore.Unity.Core.Errors
{
    public sealed class LoreError : IEquatable<LoreError>
    {
        public LoreError(ErrorCode code, string message)
        {
            if (code == ErrorCode.None)
            {
                throw new ArgumentException("An error must have a non-None error code.", nameof(code));
            }

            if (string.IsNullOrWhiteSpace(message))
            {
                throw new ArgumentException("An error message is required.", nameof(message));
            }

            Code = code;
            Message = message;
        }

        public ErrorCode Code { get; }

        public string Message { get; }

        public bool Equals(LoreError other)
        {
            return other != null && Code == other.Code && Message == other.Message;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as LoreError);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((int)Code * 397) ^ Message.GetHashCode();
            }
        }

        public override string ToString()
        {
            return $"{Code}: {Message}";
        }
    }
}
