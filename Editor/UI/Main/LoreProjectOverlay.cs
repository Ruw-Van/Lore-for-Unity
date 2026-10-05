using Lore.Unity.Core.Status;
using Lore.Unity.Integration.EditorLifecycle;
using UnityEditor;
using UnityEngine;

namespace Lore.Unity.UI.Main
{
    [InitializeOnLoad]
    public static class LoreProjectOverlay
    {
        static LoreProjectOverlay()
        {
            EditorApplication.projectWindowItemOnGUI -= Draw;
            EditorApplication.projectWindowItemOnGUI += Draw;
            AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
            AssemblyReloadEvents.beforeAssemblyReload += Unregister;
        }

        private static void Unregister()
        {
            EditorApplication.projectWindowItemOnGUI -= Draw;
            AssemblyReloadEvents.beforeAssemblyReload -= Unregister;
        }

        private static void Draw(string guid, Rect rect)
        {
            if (Event.current.type != EventType.Repaint) return;
            var index = LoreBootstrap.Assets;
            if (index == null || !index.TryGetGuid(guid, out var asset)) return;
            var entry = asset.Asset ?? asset.Meta;
            if (entry == null) return;
            var status = entry.Status;
            var color = status.Conflict == ConflictState.Conflicted ? new Color(0.9f, 0.25f, 0.2f) :
                status.Stage != StageState.Unstaged ? new Color(0.25f, 0.75f, 0.35f) :
                new Color(0.9f, 0.65f, 0.2f);
            EditorGUI.DrawRect(new Rect(rect.xMax - 5, rect.yMin + 2, 3, rect.height - 4), color);
        }
    }
}
