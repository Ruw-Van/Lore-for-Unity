using System;
using Lore.Unity.Core.Paths;

namespace Lore.Unity.Integration.Editing
{
    public enum UnityEditingKind
    {
        Scene,
        ProjectSettings,
        NonMergeableAsset,
        Script,
        MergeableTextAsset
    }

    public sealed class UnityLockPolicy
    {
        public bool AutoLockScene { get; set; } = true;
        public bool AutoLockProjectSettings { get; set; } = true;
        public bool AutoLockNonMergeableAsset { get; set; } = true;
        public bool AutoLockScript { get; set; }
        public bool AutoLockMergeableTextAsset { get; set; }

        public bool ShouldAcquire(UnityAssetPath path, bool textSerialization)
        {
            switch (Classify(path, textSerialization))
            {
                case UnityEditingKind.Scene: return AutoLockScene;
                case UnityEditingKind.ProjectSettings: return AutoLockProjectSettings;
                case UnityEditingKind.Script: return AutoLockScript;
                case UnityEditingKind.MergeableTextAsset: return AutoLockMergeableTextAsset;
                default: return AutoLockNonMergeableAsset;
            }
        }

        public static UnityEditingKind Classify(UnityAssetPath path, bool textSerialization)
        {
            if (string.IsNullOrEmpty(path.Value)) throw new ArgumentException("Asset path required.", nameof(path));
            var value = path.Value;
            if (value.StartsWith("ProjectSettings/", StringComparison.Ordinal)) return UnityEditingKind.ProjectSettings;
            if (value.EndsWith(".unity", StringComparison.OrdinalIgnoreCase)) return UnityEditingKind.Scene;
            if (value.EndsWith(".cs", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".asmdef", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".asmref", StringComparison.OrdinalIgnoreCase)) return UnityEditingKind.Script;
            if (value.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".controller", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
                return textSerialization ? UnityEditingKind.MergeableTextAsset : UnityEditingKind.NonMergeableAsset;
            if (value.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".uxml", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".uss", StringComparison.OrdinalIgnoreCase) ||
                value.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                return UnityEditingKind.MergeableTextAsset;
            // Unknown formats are treated conservatively as non-mergeable.
            return UnityEditingKind.NonMergeableAsset;
        }
    }
}
