using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;

namespace Lore.Unity.Infrastructure.Runtime
{
    // The caller holds IRuntimeLockManager across read/modify/write.
    public sealed class FileRuntimeRegistry : IRuntimeRegistry
    {
        private readonly string _file;

        public FileRuntimeRegistry(RuntimeLayout layout)
        {
            if (layout == null) throw new ArgumentNullException(nameof(layout));
            _file = Path.Combine(layout.Root.Value, "projects.registry");
        }

        public IReadOnlyList<RuntimeProjectRecord> Read()
        {
            var records = new List<RuntimeProjectRecord>();
            if (!File.Exists(_file)) return records.AsReadOnly();
            foreach (var line in File.ReadAllLines(_file, Encoding.UTF8))
            {
                var fields = line.Split('\t');
                if (fields.Length != 4) throw new InvalidDataException("Invalid runtime registry.");
                try
                {
                    records.Add(new RuntimeProjectRecord(new ProjectId(Decode(fields[0])), new AbsolutePath(Decode(fields[1])),
                        new LoreVersion(Decode(fields[2])),
                        new DateTime(long.Parse(fields[3], CultureInfo.InvariantCulture), DateTimeKind.Utc)));
                }
                catch (Exception e) when (e is FormatException || e is ArgumentException || e is OverflowException)
                {
                    throw new InvalidDataException("Invalid runtime registry.", e);
                }
            }
            return records.AsReadOnly();
        }

        public void Record(RuntimeProjectRecord project)
        {
            if (project == null) throw new ArgumentNullException(nameof(project));
            var records = new List<RuntimeProjectRecord>(Read());
            records.RemoveAll(item => item.ProjectId.Equals(project.ProjectId));
            records.Add(project);
            Directory.CreateDirectory(Path.GetDirectoryName(_file));
            var temporary = _file + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    foreach (var item in records)
                        writer.WriteLine(string.Join("\t", Encode(item.ProjectId.Value), Encode(item.ProjectPath.Value),
                            Encode(item.RequiredLoreVersion.Value), item.LastSeenUtc.Ticks.ToString(CultureInfo.InvariantCulture)));
                    writer.Flush();
                    stream.Flush(true);
                }
                if (File.Exists(_file)) File.Replace(temporary, _file, null);
                else File.Move(temporary, _file);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));
        private static string Decode(string value) => Encoding.UTF8.GetString(Convert.FromBase64String(value));
    }
}
