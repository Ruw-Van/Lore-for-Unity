using System;
using Lore.Unity.Application.Conflicts;
using NUnit.Framework;

namespace Lore.Unity.Tests.Application
{
    public sealed class ExternalMergeToolTests
    {
        [Test]
        public void RequiresAbsoluteExecutableAndAllFilePlaceholders()
        {
            var args = new[] { "--base={base}", "--mine={mine}", "--theirs={theirs}", "--result={result}" };
            Assert.Throws<ArgumentException>(() => new ExternalMergeTool("Tool", "relative.exe", args));
            Assert.Throws<ArgumentException>(() => new ExternalMergeTool("Tool", "/trusted/tool",
                new[] { "{base}", "{mine}", "{theirs}" }));
            var tool = new ExternalMergeTool("Tool", System.IO.Path.GetFullPath("tool"), args);
            Assert.That(tool.Expand("base path", "mine", "theirs", "result")[0], Is.EqualTo("--base=base path"));
            Assert.Throws<ArgumentException>(() => new ExternalMergeTool("Tool", tool.Executable,
                new[] { "{base}", "{mine}", "{theirs}", "{result}", "{password}" }));
        }
    }
}
