using System;
using System.IO;
using Lore.Unity.Application.Conflicts;
using Lore.Unity.Integration.Editing;
using NUnit.Framework;

namespace Lore.Unity.Tests.Integration
{
    public sealed class UnityExternalMergeToolSettingsTests
    {
        [Test]
        public void StoresMultiplePersonalToolsAndRejectsDuplicateNames()
        {
            var settings = UnityExternalMergeToolSettings.instance;
            var args = new[] { "{base}", "{mine}", "{theirs}", "{result}" };
            var first = new ExternalMergeTool("Alpha", Path.GetFullPath("alpha"), args);
            var second = new ExternalMergeTool("Beta", Path.GetFullPath("beta"), args);
            settings.Set(new[] { first, second });
            Assert.That(settings.Read().Value.Count, Is.EqualTo(2));
            Assert.Throws<ArgumentException>(() => settings.Set(new[] { first,
                new ExternalMergeTool("alpha", Path.GetFullPath("other"), args) }));
            Assert.That(settings.Read().Value.Count, Is.EqualTo(2));
        }
    }
}
