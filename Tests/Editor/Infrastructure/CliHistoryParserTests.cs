using Lore.Unity.Core.Identifiers;
using Lore.Unity.Infrastructure.LoreCli;
using NUnit.Framework;

namespace Lore.Unity.Tests.Infrastructure
{
    public sealed class CliHistoryParserTests
    {
        private const string Header = "{\"tagName\":\"revisionHistory\",\"data\":{\"repository\":\"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa\"}}\n";
        private const string Completed = "{\"tagName\":\"complete\",\"data\":{\"status\":0}}\n";

        [Test]
        public void ReadsLocalRevisionWithStructuredMessageAndTimestamp()
        {
            var entry = "{\"tagName\":\"revisionHistoryEntry\",\"data\":{\"revision\":\"" +
                new string('b', 64) + "\",\"revisionNumber\":3}}\n";
            var message = "{\"tagName\":\"metadata\",\"data\":{\"key\":\"message\",\"value\":{\"tagName\":\"string\",\"data\":\"Fix scene\"}}}\n";
            var timestamp = "{\"tagName\":\"metadata\",\"data\":{\"key\":\"timestamp\",\"value\":{\"tagName\":\"numeric\",\"data\":1791125757055}}}\n";
            var relay = "{\"tagName\":\"log\",\"data\":{}}\n" +
                "{\"tagName\":\"complete\",\"data\":{\"status\":111}}\n";
            var parser = new CliHistoryParser();
            var result = parser.Parse(new RepositoryId(new string('a', 32)),
                Header + entry + message + timestamp + Completed + relay);
            Assert.That(result.IsSuccess, Is.True);
            Assert.That(result.Value[0].Message, Is.EqualTo("Fix scene"));
            Assert.That(result.Value[0].Number.Value, Is.EqualTo(3L));
            Assert.That(parser.Parse(new RepositoryId("other"), Header + Completed).IsFailure, Is.True);
            Assert.That(parser.Parse(new RepositoryId(new string('a', 32)),
                Header + entry + message + Completed).IsFailure, Is.True);
        }

        [Test]
        public void EmptyLocalHistoryIsValidButIncompleteStreamIsNot()
        {
            var parser = new CliHistoryParser();
            var id = new RepositoryId(new string('a', 32));
            Assert.That(parser.Parse(id, Header + Completed).Value.Count, Is.EqualTo(0));
            Assert.That(parser.Parse(id, Header).IsFailure, Is.True);
        }
    }
}
