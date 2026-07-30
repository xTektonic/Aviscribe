namespace Aviscribe.Core.Ocr
{
    internal sealed class TalkatooConfirmationTracker
    {
        internal const int IdleDetectionIntervalFrames = 6;
        internal const int RequiredStableFrames = 3;
        internal const int RetryStableFrames = 6;

        private readonly object _lock = new();
        private TalkatooConfirmationState _state;
        private TalkatooPromptSignature? _reference;
        private long _generation;
        private int _stableFrameCount;
        private int _attemptCount;
        private bool _ocrPending;
        private bool _unresolved;

        public bool ShouldInspect(long processedFrameCount)
        {
            lock (_lock)
            {
                return _state != TalkatooConfirmationState.Idle ||
                    (processedFrameCount - 1) % IdleDetectionIntervalFrames == 0;
            }
        }

        public TalkatooConfirmationDecision Observe(
            bool present,
            TalkatooPromptSignature? signature)
        {
            lock (_lock)
            {
                if (!present || signature == null)
                {
                    ResetCore();
                    return default;
                }

                if (_state == TalkatooConfirmationState.Idle)
                {
                    StartRun(signature);
                    return default;
                }

                if (!_reference!.IsNearIdenticalTo(signature))
                {
                    StartRun(signature);
                    return default;
                }

                _stableFrameCount++;

                if (_state == TalkatooConfirmationState.Confirming &&
                    _stableFrameCount >= RequiredStableFrames &&
                    !_ocrPending)
                {
                    return new TalkatooConfirmationDecision(true, _generation, 1);
                }

                if (_state == TalkatooConfirmationState.Latched &&
                    _unresolved &&
                    !_ocrPending &&
                    _attemptCount == 1 &&
                    _stableFrameCount >= RequiredStableFrames + RetryStableFrames)
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

        private void ResetCore()
        {
            _state = TalkatooConfirmationState.Idle;
            _reference = null;
            _stableFrameCount = 0;
            _attemptCount = 0;
            _ocrPending = false;
            _unresolved = false;
        }

        private void StartRun(TalkatooPromptSignature signature)
        {
            _generation++;
            _reference = signature;
            _stableFrameCount = 1;
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
