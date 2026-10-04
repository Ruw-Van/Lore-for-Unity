using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Lore.Unity.Application.Locks;
using Lore.Unity.Application.Operations;
using Lore.Unity.Core.Identifiers;
using Lore.Unity.Core.Paths;
using Lore.Unity.Core.Results;
using Lore.Unity.Core.Operations;
using Lore.Unity.Integration.Assets;

namespace Lore.Unity.Integration.Editing
{
    public interface IUnityLockRequester
    {
        Task<WriteOutcome> AcquireAsync(RepositoryId repository, RepositoryPath path, CancellationToken token);
    }

    public sealed class LoreLockRequester : IUnityLockRequester
    {
        private readonly LockService _service;
        public LoreLockRequester(LockService service) { _service = service ?? throw new ArgumentNullException(nameof(service)); }
        public Task<WriteOutcome> AcquireAsync(RepositoryId repository, RepositoryPath path, CancellationToken token) =>
            _service.AcquireAsync(repository, path, token);
    }

    public sealed class UnityLockIntentResult
    {
        public UnityLockIntentResult(bool requested, WriteOutcome outcome)
        {
            Requested = requested;
            Outcome = outcome;
        }
        public bool Requested { get; }
        public WriteOutcome Outcome { get; }
    }

    public sealed class UnityLockIntentController
    {
        private readonly IUnityLockRequester _requester;
        private readonly RepositoryId _repository;
        private readonly UnityAssetPathMapper _paths;
        private readonly Func<UnityLockPolicy> _policy;
        private readonly Dictionary<RepositoryPath, Task<UnityLockIntentResult>> _inFlight =
            new Dictionary<RepositoryPath, Task<UnityLockIntentResult>>();
        private readonly object _mutex = new object();

        public UnityLockIntentController(IUnityLockRequester requester, RepositoryId repository,
            UnityAssetPathMapper paths, UnityLockPolicy policy)
            : this(requester, repository, paths, FixedPolicy(policy)) { }

        public UnityLockIntentController(IUnityLockRequester requester, RepositoryId repository,
            UnityAssetPathMapper paths, Func<UnityLockPolicy> policy)
        {
            _requester = requester ?? throw new ArgumentNullException(nameof(requester));
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _paths = paths ?? throw new ArgumentNullException(nameof(paths));
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        // Must be invoked outside Unity's edit callback. Skipped policy is an
        // explicit successful outcome, not an implicit network request.
        public Task<UnityLockIntentResult> RequestAsync(UnityAssetPath path, bool textSerialization, CancellationToken token)
        {
            var policy = _policy();
            if (policy == null)
                return Task.FromResult(new UnityLockIntentResult(false,
                    new WriteOutcome(new OperationId(Guid.NewGuid().ToString("N")),
                        Result.Failure(new Lore.Unity.Core.Errors.LoreError(
                            Lore.Unity.Core.Errors.ErrorCode.ValidationFailed, "Lock policy is unavailable.")),
                        OperationState.Failed)));
            if (!policy.ShouldAcquire(path, textSerialization))
                return Task.FromResult(new UnityLockIntentResult(false, null));
            var mapped = _paths.ToRepositoryPath(path);
            if (mapped.IsFailure)
                return Task.FromResult(new UnityLockIntentResult(false,
                    new WriteOutcome(new OperationId(Guid.NewGuid().ToString("N")),
                        Result.Failure(mapped.Error), OperationState.Failed)));
            lock (_mutex)
            {
                if (_inFlight.TryGetValue(mapped.Value, out var current)) return current;
                var request = AcquireAsync(mapped.Value, token);
                _inFlight[mapped.Value] = request;
                var key = mapped.Value;
                _ = request.ContinueWith(completed =>
                {
                    lock (_mutex)
                        if (_inFlight.TryGetValue(key, out var pending) && ReferenceEquals(pending, completed))
                            _inFlight.Remove(key);
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                return request;
            }
        }

        private async Task<UnityLockIntentResult> AcquireAsync(RepositoryPath path, CancellationToken token) =>
            new UnityLockIntentResult(true, await _requester.AcquireAsync(_repository, path, token));

        private static Func<UnityLockPolicy> FixedPolicy(UnityLockPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            return () => policy;
        }
    }
}
