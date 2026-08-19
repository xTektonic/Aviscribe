using Aviscribe.Core.Capture;

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
        private DateTime? _lastInspection;
        private DateTime? _presentSince;
        private DateTime? _absentSince;
        private DateTime _lastObservation;
        private DateTime _syntheticSmokeTimestamp;
        private int _attemptCount;
        private bool _ocrPending;
        private bool _unresolved;
        private bool _resolved;

        public CollectionConfirmationTracker(CollectionConfirmationProfile profile)
        {
            _profile = profile;
            _syntheticSmokeTimestamp =
                DateTime.UnixEpoch - profile.DetectionInterval;
        }

        public bool ShouldInspect(DateTime timestamp)
        {
            lock (_lock)
            {
                if (_lastInspection == null ||
                    timestamp < _lastInspection.Value ||
                    CaptureTiming.HasElapsed(
                        timestamp - _lastInspection.Value,
                        _profile.DetectionInterval))
                {
                    _lastInspection = timestamp;
                    return true;
                }

                return false;
            }
        }

        // Deterministic compatibility for classifier audits. Runtime callers
        // use the timestamp overload.
        public bool ShouldInspect(long preferredRateFrameNumber) =>
            ShouldInspect(
                DateTime.UnixEpoch + CaptureTiming.DurationForFrames(
                    checked((int)Math.Max(0, preferredRateFrameNumber - 1))));

        // Deterministic compatibility for classifier audits. Runtime callers
        // use the timestamp overload.
        public CollectionConfirmationSnapshot Observe(bool present)
        {
            _syntheticSmokeTimestamp += _profile.DetectionInterval;
            return Observe(present, _syntheticSmokeTimestamp);
        }

        public CollectionConfirmationSnapshot Observe(bool present, DateTime timestamp)
        {
            lock (_lock)
            {
                _lastInspection = timestamp;
                _lastObservation = timestamp;
                if (!present)
                {
                    _consecutivePresent = 0;
                    _presentSince = null;
                    if (_state == CollectionConfirmationState.Idle)
                        return SnapshotCore();

                    _absentSince ??= timestamp;
                    _consecutiveAbsent++;
                    if (_consecutiveAbsent >= 2 &&
                        CaptureTiming.HasElapsed(
                            ElapsedSince(_absentSince, timestamp),
                            _profile.RequiredAbsentDuration))
                    {
                        ReleaseCore();
                    }

                    return SnapshotCore();
                }

                if (_state == CollectionConfirmationState.Idle)
                    StartGeneration(timestamp);

                _consecutiveAbsent = 0;
                _absentSince = null;
                _presentSince ??= timestamp;
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
                    RetryReadyCore();
                if (!validInitial && !validRetry)
                    return false;

                _attemptCount = attempt;
                _ocrPending = true;
                _unresolved = false;
                _state = CollectionConfirmationState.Latched;
                _consecutivePresent = 0;
                _presentSince = null;
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
                _presentSince = null;
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
            {
                ReleaseCore();
                _lastInspection = null;
                _lastObservation = default;
            }
        }

        private bool IsConfirmedCore()
        {
            return _state == CollectionConfirmationState.Latched ||
                (_state == CollectionConfirmationState.Confirming &&
                 _consecutivePresent >= Math.Min(
                     2,
                     _profile.RequiredPresentObservations) &&
                 CaptureTiming.HasElapsed(
                     ElapsedSince(_presentSince, _lastObservation),
                     _profile.RequiredPresentDuration));
        }

        private bool RetryReadyCore()
        {
            return _consecutivePresent >= 2 &&
                CaptureTiming.HasElapsed(
                    ElapsedSince(_presentSince, _lastObservation),
                    _profile.RetryPresentDuration);
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
                ElapsedSince(_presentSince, _lastObservation),
                ElapsedSince(_absentSince, _lastObservation),
                _lastObservation,
                _attemptCount,
                _ocrPending,
                _unresolved,
                _resolved);
        }

        private void StartGeneration(DateTime timestamp)
        {
            _generation++;
            _state = CollectionConfirmationState.Confirming;
            _consecutivePresent = 0;
            _consecutiveAbsent = 0;
            _presentObservationCount = 0;
            _presentSince = timestamp;
            _absentSince = null;
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
            _presentSince = null;
            _absentSince = null;
            _attemptCount = 0;
            _ocrPending = false;
            _unresolved = false;
            _resolved = false;
        }

        private static TimeSpan ElapsedSince(
            DateTime? since,
            DateTime timestamp)
        {
            return since is { } value && timestamp >= value
                ? timestamp - value
                : TimeSpan.Zero;
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
        TimeSpan ConsecutivePresentDuration,
        TimeSpan ConsecutiveAbsentDuration,
        DateTime LastObservation,
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
            ConsecutivePresent >= 2 &&
            CaptureTiming.HasElapsed(
                ConsecutivePresentDuration,
                profile.RetryPresentDuration);
    }
}
