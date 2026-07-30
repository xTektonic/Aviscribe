namespace Aviscribe.Core.Ocr
{
    internal sealed class CollectionConfirmationTracker
    {
        private readonly object _lock = new();
        private readonly CollectionConfirmationProfile _profile;
        private CollectionConfirmationState _state;
        private long _generation;
        private int _consecutivePresent;
        private int _consecutiveAbsent;
        private int _presentObservationCount;
        private int _attemptCount;
        private bool _ocrPending;
        private bool _unresolved;
        private bool _resolved;

        public CollectionConfirmationTracker(CollectionConfirmationProfile profile)
        {
            _profile = profile;
        }

        public bool ShouldInspect(long processedFrameCount)
        {
            return (processedFrameCount - 1) %
                Math.Max(1, _profile.DetectionIntervalFrames) == 0;
        }

        public CollectionConfirmationSnapshot Observe(bool present)
        {
            lock (_lock)
            {
                if (!present)
                {
                    _consecutivePresent = 0;
                    if (_state == CollectionConfirmationState.Idle)
                        return SnapshotCore();

                    _consecutiveAbsent++;
                    if (_consecutiveAbsent >= _profile.RequiredAbsentObservations)
                        ReleaseCore();

                    return SnapshotCore();
                }

                if (_state == CollectionConfirmationState.Idle)
                    StartGeneration();

                _consecutiveAbsent = 0;
                _consecutivePresent++;
                _presentObservationCount++;
                return SnapshotCore();
            }
        }

        public CollectionConfirmationSnapshot Snapshot()
        {
            lock (_lock)
                return SnapshotCore();
        }

        public bool RecordEnqueued(long generation, int attempt)
        {
            lock (_lock)
            {
                if (generation != _generation || _ocrPending)
                    return false;

                var validInitial = attempt == 1 &&
                    _attemptCount == 0 &&
                    IsConfirmedCore();
                var validRetry = attempt == 2 &&
                    _attemptCount == 1 &&
                    _unresolved &&
                    _consecutivePresent >= _profile.RetryPresentObservations;
                if (!validInitial && !validRetry)
                    return false;

                _attemptCount = attempt;
                _ocrPending = true;
                _unresolved = false;
                _state = CollectionConfirmationState.Latched;
                _consecutivePresent = 0;
                return true;
            }
        }

        public void RecordOutcome(long generation, bool resolved)
        {
            lock (_lock)
            {
                if (generation != _generation ||
                    _state == CollectionConfirmationState.Idle)
                {
                    return;
                }

                _ocrPending = false;
                _resolved = resolved;
                _unresolved = !resolved && _attemptCount < 2;
                _consecutivePresent = 0;
                _state = CollectionConfirmationState.Latched;
            }
        }

        public void Suppress(long generation)
        {
            lock (_lock)
            {
                if (generation != _generation ||
                    _state == CollectionConfirmationState.Idle)
                {
                    return;
                }

                _ocrPending = false;
                _unresolved = false;
                _resolved = true;
                _state = CollectionConfirmationState.Latched;
            }
        }

        public void FinalizeUnresolved(long generation)
        {
            lock (_lock)
            {
                if (generation != _generation ||
                    _state == CollectionConfirmationState.Idle)
                {
                    return;
                }

                _ocrPending = false;
                _unresolved = false;
                _resolved = false;
                _state = CollectionConfirmationState.Latched;
            }
        }

        public void Reset()
        {
            lock (_lock)
                ReleaseCore();
        }

        private bool IsConfirmedCore()
        {
            return _state == CollectionConfirmationState.Latched ||
                (_state == CollectionConfirmationState.Confirming &&
                 _consecutivePresent >= _profile.RequiredPresentObservations);
        }

        private CollectionConfirmationSnapshot SnapshotCore()
        {
            return new CollectionConfirmationSnapshot(
                _profile.RegionType,
                _state,
                _generation,
                _state != CollectionConfirmationState.Idle &&
                    _consecutiveAbsent == 0,
                IsConfirmedCore(),
                _consecutivePresent,
                _consecutiveAbsent,
                _presentObservationCount,
                _attemptCount,
                _ocrPending,
                _unresolved,
                _resolved);
        }

        private void StartGeneration()
        {
            _generation++;
            _state = CollectionConfirmationState.Confirming;
            _consecutivePresent = 0;
            _consecutiveAbsent = 0;
            _presentObservationCount = 0;
            _attemptCount = 0;
            _ocrPending = false;
            _unresolved = false;
            _resolved = false;
        }

        private void ReleaseCore()
        {
            _state = CollectionConfirmationState.Idle;
            _consecutivePresent = 0;
            _consecutiveAbsent = 0;
            _presentObservationCount = 0;
            _attemptCount = 0;
            _ocrPending = false;
            _unresolved = false;
            _resolved = false;
        }
    }

    internal enum CollectionConfirmationState
    {
        Idle,
        Confirming,
        Latched
    }

    internal readonly record struct CollectionConfirmationSnapshot(
        OcrRegionType RegionType,
        CollectionConfirmationState State,
        long Generation,
        bool CurrentlyPresent,
        bool Confirmed,
        int ConsecutivePresent,
        int ConsecutiveAbsent,
        int PresentObservationCount,
        int AttemptCount,
        bool OcrPending,
        bool Unresolved,
        bool Resolved)
    {
        public bool Active => State != CollectionConfirmationState.Idle;

        public bool CanEnqueueInitial =>
            State == CollectionConfirmationState.Confirming &&
            Confirmed &&
            AttemptCount == 0 &&
            !OcrPending;

        public bool CanEnqueueRetry(CollectionConfirmationProfile profile) =>
            State == CollectionConfirmationState.Latched &&
            Unresolved &&
            AttemptCount == 1 &&
            !OcrPending &&
            ConsecutivePresent >= profile.RetryPresentObservations;
    }
}
