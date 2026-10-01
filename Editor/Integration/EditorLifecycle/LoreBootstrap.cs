using Lore.Unity.Application.Runtime;
using UnityEditor;

namespace Lore.Unity.Integration.EditorLifecycle
{
    // Composition root only. A validated manifest and installed runtime are required
    // before a backend can be constructed; neither is bundled in this package yet.
    [InitializeOnLoad]
    public static class LoreBootstrap
    {
        private static RuntimeContext _context;

        static LoreBootstrap()
        {
            _context = new RuntimeContext(RuntimeAvailability.SetupRequired);
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeReload;
        }

        public static RuntimeContext Context => _context;

        private static void OnBeforeReload()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeReload;
            _context = null;
        }
    }
}
