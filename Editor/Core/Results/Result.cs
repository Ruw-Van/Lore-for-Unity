using System;
using Lore.Unity.Core.Errors;

namespace Lore.Unity.Core.Results
{
    public readonly struct Result
    {
        private Result(bool isSuccess, LoreError error)
        {
            IsSuccess = isSuccess;
            Error = error;
        }

        public bool IsSuccess { get; }

        public bool IsFailure => !IsSuccess;

        public LoreError Error { get; }

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
            Error = null;
        }

        private Result(LoreError error)
        {
            IsSuccess = false;
            _value = default;
            Error = error;
        }

        public bool IsSuccess { get; }

        public bool IsFailure => !IsSuccess;

        public LoreError Error { get; }

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
