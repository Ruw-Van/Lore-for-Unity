using Lore.Unity.Core.Paths;
using Lore.Unity.Integration.Editing;
using NUnit.Framework;

namespace Lore.Unity.Tests.Integration
{
    public sealed class UnityLockPolicyTests
    {
        [Test]
        public void DefaultsFollowSceneProjectSettingsAndNonMergeablePolicy()
        {
            var policy = new UnityLockPolicy();
            Assert.That(policy.ShouldAcquire(new UnityAssetPath("Assets/Level.unity"), true), Is.True);
            Assert.That(policy.ShouldAcquire(new UnityAssetPath("ProjectSettings/ProjectSettings.asset"), true), Is.True);
            Assert.That(policy.ShouldAcquire(new UnityAssetPath("Assets/Texture.png"), true), Is.True);
            Assert.That(policy.ShouldAcquire(new UnityAssetPath("Assets/Model.prefab"), true), Is.False);
            Assert.That(policy.ShouldAcquire(new UnityAssetPath("Assets/Code.cs"), true), Is.False);
        }

        [Test]
        public void PrefabIsNonMergeableWhenSerializationIsNotText()
        {
            var policy = new UnityLockPolicy();
            Assert.That(policy.ShouldAcquire(new UnityAssetPath("Assets/Model.prefab"), false), Is.True);
            policy.AutoLockNonMergeableAsset = false;
            Assert.That(policy.ShouldAcquire(new UnityAssetPath("Assets/Model.prefab"), false), Is.False);
        }
    }
}
