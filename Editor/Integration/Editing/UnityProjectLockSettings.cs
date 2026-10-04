using UnityEditor;
using UnityEngine;

namespace Lore.Unity.Integration.Editing
{
    [FilePath("ProjectSettings/LoreForUnitySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public sealed class UnityProjectLockSettings : ScriptableSingleton<UnityProjectLockSettings>
    {
        [SerializeField] private bool autoLockScene = true;
        [SerializeField] private bool autoLockProjectSettings = true;
        [SerializeField] private bool autoLockNonMergeableAsset = true;
        [SerializeField] private bool autoLockScript;
        [SerializeField] private bool autoLockMergeableTextAsset;

        public UnityLockPolicy ReadPolicy() => new UnityLockPolicy
        {
            AutoLockScene = autoLockScene,
            AutoLockProjectSettings = autoLockProjectSettings,
            AutoLockNonMergeableAsset = autoLockNonMergeableAsset,
            AutoLockScript = autoLockScript,
            AutoLockMergeableTextAsset = autoLockMergeableTextAsset
        };

        public void SetPolicy(UnityLockPolicy policy)
        {
            if (policy == null) throw new System.ArgumentNullException(nameof(policy));
            autoLockScene = policy.AutoLockScene;
            autoLockProjectSettings = policy.AutoLockProjectSettings;
            autoLockNonMergeableAsset = policy.AutoLockNonMergeableAsset;
            autoLockScript = policy.AutoLockScript;
            autoLockMergeableTextAsset = policy.AutoLockMergeableTextAsset;
            Save(true);
        }
    }
}
