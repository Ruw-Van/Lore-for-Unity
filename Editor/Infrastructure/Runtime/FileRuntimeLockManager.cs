using System;
using System.IO;

namespace Lore.Unity.Infrastructure.Runtime
{
    public sealed class FileRuntimeLockManager : IRuntimeLockManager
    {
        private readonly RuntimeLayout _layout;

        public FileRuntimeLockManager(RuntimeLayout layout)
        {
            _layout = layout ?? throw new ArgumentNullException(nameof(layout));
        }

        public IDisposable TryAcquire()
        {
            Directory.CreateDirectory(_layout.Root.Value);
            try
            {
                return new FileStream(Path.Combine(_layout.Root.Value, ".manager.lock"),
                    FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) { return null; }
        }
    }
}
