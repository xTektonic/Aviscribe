using Aviscribe.Core.Capture;

namespace Aviscribe.Core.Ocr
{
    internal sealed class TalkatooConfirmationTracker
    {
        internal const int IdleDetectionIntervalFrames = 6;
        internal const int RequiredStableFrames = 3;
        internal const int RetryStableFrames = 6;
        internal static TimeSpan IdleDetectionInterval { get; } =
            CaptureTiming.DurationForFrames(IdleDetectionIntervalFrames);
        internal static TimeSpan RequiredStableDuration { get; } =
            CaptureTiming.DurationBetweenObservations(RequiredStableFrames);
        internal static TimeSpan MaximumAcquisitionDuration { get; } =
            CaptureTiming.DurationForFrames(IdleDetectionIntervalFrames);
        internal static TimeSpan SparseSamplingInterval { get; } =
            CaptureTiming.DurationForFrames(3);
        internal static TimeSpan RetryStableDuration { get; } =
            CaptureTiming.DurationForFrames(RetryStableFrames);

        private readonly object _lock = new();
        private TalkatooConfirmationState _state;
        private TalkatooPromptSignature? _reference;
        private long _generation;
        private int _stableObservationCount;
        private DateTime _stableSince;
        private int _continuousPresentObservationCount;
        private DateTime _continuousPresentSince;
        private DateTime _lastPresentTimestamp;
        private DateTime? _lastInspection;
        private int _attemptCount;
        private bool _ocrPending;
        private bool _unresolved;
        private DateTime _syntheticSmokeTimestamp =
            DateTime.UnixEpoch - CaptureTiming.PreferredFrameInterval;

        // Retained for deterministic classifier smoke coverage. Runtime callers
        // always pass the source frame timestamp.
        public bool ShouldInspect(long preferredRateFrameNumber) =>
            ShouldInspect(
                DateTime.UnixEpoch + CaptureTiming.DurationForFrames(
                    checked((int)Math.Max(0, preferredRateFrameNumber - 1))));

        // Retained for deterministic classifier smoke coverage. Runtime callers
        // always pass the source frame timestamp.
        public TalkatooConfirmationDecision Observe(
            bool present,
            TalkatooPromptSignature? signature)
        {
            _syntheticSmokeTimestamp += CaptureTiming.PreferredFrameInterval;
            return Observe(present, signature, _syntheticSmokeTimestamp);
        }

        public bool ShouldInspect(DateTime timestamp)
        {
            lock (_lock)
            {
                if (_state != TalkatooConfirmationState.Idle)
                {
                    _lastInspection = timestamp;
                    return true;
                }

                if (_lastInspection == null ||
                    timestamp < _lastInspection.Value ||
                    CaptureTiming.HasElapsed(
                        timestamp - _lastInspection.Value,
                        IdleDetectionInterval))
                {
                    _lastInspection = timestamp;
                    return true;
                }

                return false;
            }
        }

        public TalkatooConfirmationDecision Observe(
            bool present,
            TalkatooPromptSignature? signature,
            DateTime timestamp)
        {
            lock (_lock)
            {
                _lastInspection = timestamp;
                if (!present || signature == null)
                {
                    ResetCore(resetCadence: false);
                    return default;
                }

                if (_state == TalkatooConfirmationState.Idle)
                {
                    StartRun(signature, timestamp, resetContinuousRun: true);
                    return default;
                }

                var sampleInterval = timestamp >= _lastPresentTimestamp
                    ? timestamp - _lastPresentTimestamp
                    : TimeSpan.Zero;
                _lastPresentTimestamp = timestamp;
                _continuousPresentObservationCount++;
                if (!_reference!.IsNearIdenticalTo(signature))
                {
                    var resetContinuousRun =
                        _state == TalkatooConfirmationState.Latched;
                    StartRun(signature, timestamp, resetContinuousRun);
                }
                else
                {
                    _stableObservationCount++;
                }

                var stableDuration = timestamp >= _stableSince
                    ? timestamp - _stableSince
                    : TimeSpan.Zero;
                var continuousPresentDuration =
                    timestamp >= _continuousPresentSince
                        ? timestamp - _continuousPresentSince
                        : TimeSpan.Zero;
                var stableEnough =
                    _stableObservationCount >= 2 &&
                    CaptureTiming.HasElapsed(
                        stableDuration,
                        RequiredStableDuration);
                var acquiredLongEnough =
                    _continuousPresentObservationCount >= 2 &&
                    CaptureTiming.HasElapsed(
                        sampleInterval,
                        SparseSamplingInterval) &&
                    CaptureTiming.HasElapsed(
                        continuousPresentDuration,
                        MaximumAcquisitionDuration);

                if (_state == TalkatooConfirmationState.Confirming &&
                    (stableEnough || acquiredLongEnough) &&
                    !_ocrPending)
                {
                    return new TalkatooConfirmationDecision(true, _generation, 1);
                }

                if (_state == TalkatooConfirmationState.Latched &&
                    _unresolved &&
                    !_ocrPending &&
                    _attemptCount == 1 &&
                    _stableObservationCount >= 2 &&
                    CaptureTiming.HasElapsed(
                        stableDuration,
                        RequiredStableDuration + RetryStableDuration))
                {
                    return new TalkatooConfirmationDecision(true, _generation, 2);
                }

                return default;
            }
        }

        public bool RecordEnqueued(long generation, int attempt)
        {
            lock (_lock)
            {
                if (generation != _generation || attempt != _attemptCount + 1)
                    return false;

                _attemptCount = attempt;
                _ocrPending = true;
                _unresolved = false;
                _state = TalkatooConfirmationState.Latched;
                return true;
            }
        }

        public void RecordResolved(long generation)
        {
            lock (_lock)
            {
                if (generation != _generation)
                    return;

                _ocrPending = false;
                _unresolved = false;
            }
        }

        public void RecordUnresolved(long generation)
        {
            lock (_lock)
            {
                if (generation != _generation)
                    return;

                _ocrPending = false;
                _unresolved = true;
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                ResetCore();
            }
        }

        private void ResetCore(bool resetCadence = true)
        {
            _state = TalkatooConfirmationState.Idle;
            _reference = null;
            _stableObservationCount = 0;
            _stableSince = default;
            _continuousPresentObservationCount = 0;
            _continuousPresentSince = default;
            _lastPresentTimestamp = default;
            if (resetCadence)
                _lastInspection = null;
            _attemptCount = 0;
            _ocrPending = false;
            _unresolved = false;
        }

        private void StartRun(
            TalkatooPromptSignature signature,
            DateTime timestamp,
            bool resetContinuousRun)
        {
            _generation++;
            _reference = signature;
            _stableObservationCount = 1;
            _stableSince = timestamp;
            if (resetContinuousRun)
            {
                _continuousPresentObservationCount = 1;
                _continuousPresentSince = timestamp;
                _lastPresentTimestamp = timestamp;
            }
            _attemptCount = 0;
            _ocrPending = false;
            _unresolved = false;
            _state = TalkatooConfirmationState.Confirming;
        }

        private enum TalkatooConfirmationState
        {
            Idle,
            Confirming,
            Latched
        }
    }

    internal readonly record struct TalkatooConfirmationDecision(
        bool ShouldEnqueue,
        long Generation,
        int Attempt);
}
