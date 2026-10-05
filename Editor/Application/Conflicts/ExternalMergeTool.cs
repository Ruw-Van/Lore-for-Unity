using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;

namespace Lore.Unity.Application.Conflicts
{
    // Arguments are one token per line in the UI. ProcessStartInfo.ArgumentList
    // handles quoting; no shell, PATH lookup, or command-line splitting.
    public sealed class ExternalMergeTool
    {
        public ExternalMergeTool(string name, string executable, IReadOnlyList<string> arguments)
        {
            if (string.IsNullOrWhiteSpace(name) || name.Length > 80 || name.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("A short tool name is required.", nameof(name));
            if (string.IsNullOrWhiteSpace(executable) || !Path.IsPathFullyQualified(executable) ||
                executable.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new ArgumentException("An absolute executable path is required.", nameof(executable));
            if (arguments == null || arguments.Count == 0 || arguments.Count > 32)
                throw new ArgumentException("Specify one argument per line.", nameof(arguments));
            var copy = new List<string>();
            var found = new HashSet<string>();
            foreach (var argument in arguments)
            {
                if (string.IsNullOrEmpty(argument) || argument.Length > 1024 ||
                    argument.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                    throw new ArgumentException("Invalid argument token.", nameof(arguments));
                foreach (var key in new[] { "{base}", "{mine}", "{theirs}", "{result}" })
                    if (argument.Contains(key)) found.Add(key);
                if (argument.Contains("{") || argument.Contains("}"))
                {
                    var rest = argument;
                    foreach (var key in new[] { "{base}", "{mine}", "{theirs}", "{result}" })
                        rest = rest.Replace(key, string.Empty);
                    if (rest.Contains("{") || rest.Contains("}"))
                        throw new ArgumentException("Unknown template placeholder.", nameof(arguments));
                }
                copy.Add(argument);
            }
            if (found.Count != 4) throw new ArgumentException("All four merge file placeholders are required.", nameof(arguments));
            Name = name;
            Executable = executable;
            Arguments = new ReadOnlyCollection<string>(copy);
        }

        public string Name { get; }
        public string Executable { get; }
        public IReadOnlyList<string> Arguments { get; }

        public IReadOnlyList<string> Expand(string basePath, string mine, string theirs, string result)
        {
            var expanded = new List<string>(Arguments.Count);
            foreach (var argument in Arguments)
                expanded.Add(argument.Replace("{base}", basePath).Replace("{mine}", mine)
                    .Replace("{theirs}", theirs).Replace("{result}", result));
            return expanded;
        }
    }
}
