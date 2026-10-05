using System;
using Lore.Unity.Application.Diagnostics;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class DiagnosticsSummaryTests
    {
        [Test]
        public void CopyContainsOnlyAllowlistedMetadata()
        {
            var summary = new DiagnosticsSummary("0.1.0", "2022.3.0f1", "0.10.0",
                "Ready", true, false, 42, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), 1, 3);
            var text = summary.Export();
            Assert.That(text.Contains("Status generation: 42"), Is.True);
            Assert.That(text.Contains("Pending recovery: 1"), Is.True);
            Assert.That(text.Contains("Repository detected: True"), Is.True);
            Assert.That(text.Contains("C:\\"), Is.False);
            Assert.That(text.Contains("token="), Is.False);
            Assert.That(new DiagnosticsSummary("https://secret/path", "2022.3", "0.10.0",
                "private", false, true, null, null, -1, -1).Export().Contains("https://"), Is.False);
        }
    }
}
