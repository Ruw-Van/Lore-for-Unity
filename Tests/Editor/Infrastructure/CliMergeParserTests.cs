using Lore.Unity.Core.Errors;
using Lore.Unity.Infrastructure.LoreCli;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class CliMergeParserTests
    {
        private static string Begin => "{\"tagName\":\"branchMergeStartBegin\",\"data\":{\"branch\":\"" +
            new string('a', 32) + "\"}}\n";
        private static string End(int conflicts) => "{\"tagName\":\"branchMergeStartEnd\",\"data\":{\"signature\":\"" +
            new string('b', 64) + "\",\"hasConflicts\":" + conflicts + "}}\n";
        private const string File = "{\"tagName\":\"branchMergeConflictFile\",\"data\":{\"path\":\"Assets/a.prefab\"}}\n";
        private const string Complete = "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n";

        [Test]
        public void NativeConflictIsNotReportedAsSuccessfulMerge()
        {
            var parser = new CliMergeParser();
            Assert.That(parser.Parse(Begin + End(1) + File + Complete).Error.Code, Is.EqualTo(ErrorCode.Conflict));
            Assert.That(parser.Parse(Begin + End(0) + Complete).IsSuccess, Is.True);
            Assert.That(parser.Parse(Begin + End(1) + Complete).Error.Code, Is.EqualTo(ErrorCode.Conflict));
            Assert.That(parser.Parse(Begin + End(0) + File + Complete).IsFailure, Is.True);
        }
    }
}
