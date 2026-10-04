using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Operations;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Integration.Assets;
using Lore.Unity.Integration.Editing;
using NUnit.Framework;

namespace Lore.Unity.Tests.Integration
{
    public sealed class UnityLockIntentControllerTests
    {
        private sealed class Requester : IUnityLockRequester
        {
            public int Calls;
            public readonly TaskCompletionSource<WriteOutcome> Pending = new TaskCompletionSource<WriteOutcome>();
            public Task<WriteOutcome> AcquireAsync(RepositoryId repository, RepositoryPath path, CancellationToken token)
            {
                Calls++;
                return Pending.Task;
            }
        }

        [Test]
        public void SameAssetLockIntentIsSingleFlight()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-lock-intent-" + Guid.NewGuid().ToString("N"));
            var requester = new Requester();
            var controller = new UnityLockIntentController(requester, new RepositoryId("repo"),
                new UnityAssetPathMapper(new AbsolutePath(root), new AbsolutePath(root)), new UnityLockPolicy());
            var path = new UnityAssetPath("Assets/Level.unity");
            var first = controller.RequestAsync(path, true, CancellationToken.None);
            var second = controller.RequestAsync(path, true, CancellationToken.None);
            Assert.That(first, Is.SameAs(second));
            Assert.That(requester.Calls, Is.EqualTo(1));
            requester.Pending.SetResult(new WriteOutcome(new OperationId("op"), Result.Success(), OperationState.Completed));
            Assert.That(first.GetAwaiter().GetResult().Requested, Is.True);
        }

        [Test]
        public void MergeablePrefabDoesNotClaimALockWasAcquired()
        {
            var root = Path.Combine(Path.GetTempPath(), "lore-lock-intent-" + Guid.NewGuid().ToString("N"));
            var requester = new Requester();
            var controller = new UnityLockIntentController(requester, new RepositoryId("repo"),
                new UnityAssetPathMapper(new AbsolutePath(root), new AbsolutePath(root)), new UnityLockPolicy());
            var result = controller.RequestAsync(new UnityAssetPath("Assets/Prefab.prefab"), true,
                CancellationToken.None).GetAwaiter().GetResult();
            Assert.That(result.Requested, Is.False);
            Assert.That(result.Outcome == null, Is.True);
            Assert.That(requester.Calls, Is.EqualTo(0));
        }
    }
}
