using UnityEditor;

namespace Lore.Unity.Integration.Editing
{
    public static class UnityLockSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider Create()
        {
            return new SettingsProvider("Project/Lore", SettingsScope.Project)
            {
                label = "Lore",
                guiHandler = _ =>
                {
                    var settings = UnityProjectLockSettings.instance;
                    var policy = settings.ReadPolicy();
                    EditorGUI.BeginChangeCheck();
                    policy.AutoLockScene = EditorGUILayout.Toggle("Auto Lock Scenes", policy.AutoLockScene);
                    policy.AutoLockProjectSettings = EditorGUILayout.Toggle("Auto Lock Project Settings", policy.AutoLockProjectSettings);
                    policy.AutoLockNonMergeableAsset = EditorGUILayout.Toggle("Auto Lock Non-mergeable Assets", policy.AutoLockNonMergeableAsset);
                    policy.AutoLockScript = EditorGUILayout.Toggle("Auto Lock Scripts", policy.AutoLockScript);
                    policy.AutoLockMergeableTextAsset = EditorGUILayout.Toggle("Auto Lock Mergeable Text", policy.AutoLockMergeableTextAsset);
                    if (EditorGUI.EndChangeCheck()) settings.SetPolicy(policy);
                }
            };
        }
    }
}
