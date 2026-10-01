using System;
using Lore.Unity.Core.Errors;
using Lore.Unity.Core.Results;
using NUnit.Framework;

namespace Lore.Unity.Tests.Core
{
    public sealed class ResultTests
    {
        [Test]
        public void SuccessContainsValue()
        {
            var result = Result<int>.Success(42);

            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value, Is.EqualTo(42));
        }

        [Test]
        public void FailureContainsErrorAndRejectsValueAccess()
        {
            var error = new LoreError(ErrorCode.ValidationFailed, "Invalid value.");
            var result = Result<int>.Failure(error);

            Assert.That(result.IsFailure, Is.True);
            Assert.That(result.Error, Is.SameAs(error));
            Assert.Throws<InvalidOperationException>(() => _ = result.Value);
        }
    }
}
