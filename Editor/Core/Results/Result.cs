using System;
using Lore.Unity.Core.Errors;

namespace Lore.Unity.Core.Results
{
    public readonly struct Result
    {
        private Result(bool isSuccess, LoreError error)
        {
            IsSuccess = isSuccess;
            _error = error;
        }

        public bool IsSuccess { get; }

        public bool IsFailure => !IsSuccess;

        private readonly LoreError _error;

        public LoreError Error => IsFailure ? _error ?? new LoreError(ErrorCode.Unknown, "Uninitialized result.") : null;

        public static Result Success()
        {
            return new Result(true, null);
        }

        public static Result Failure(LoreError error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new Result(false, error);
        }
    }

    public readonly struct Result<T>
    {
        private readonly T _value;

        private Result(T value)
        {
            IsSuccess = true;
            _value = value;
            _error = null;
        }

        private Result(LoreError error)
        {
            IsSuccess = false;
            _value = default;
            _error = error;
        }

        public bool IsSuccess { get; }

        public bool IsFailure => !IsSuccess;

        private readonly LoreError _error;

        public LoreError Error => IsFailure ? _error ?? new LoreError(ErrorCode.Unknown, "Uninitialized result.") : null;

        public T Value
        {
            get
            {
                if (IsFailure)
                {
                    throw new InvalidOperationException("A failed result has no value.");
                }

                return _value;
            }
        }

        public static Result<T> Success(T value)
        {
            return new Result<T>(value);
        }

        public static Result<T> Failure(LoreError error)
        {
            if (error == null)
            {
                throw new ArgumentNullException(nameof(error));
            }

            return new Result<T>(error);
        }
    }
}
