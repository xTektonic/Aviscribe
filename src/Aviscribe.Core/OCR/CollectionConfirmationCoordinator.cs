using Aviscribe.Core.Capture;

namespace Aviscribe.Core.Ocr
{
    internal sealed class CollectionConfirmationCoordinator
    {
        private readonly object _lock = new();
        private readonly Dictionary<OcrRegionType, CollectionConfirmationTracker> _trackers =
            new()
            {
                [OcrRegionType.MoonGet] = new(CollectionConfirmationProfile.MoonGet),
                [OcrRegionType.StoryMoon] = new(CollectionConfirmationProfile.StoryMoon)
            };
        private CollectionEvent? _event;

        public bool ShouldInspect(OcrRegionType regionType, DateTime timestamp)
        {
            return _trackers[regionType].ShouldInspect(timestamp);
        }

        public void Observe(
            OcrRegionType regionType,
            bool present,
            DateTime timestamp)
        {
            lock (_lock)
                _trackers[regionType].Observe(present, timestamp);
        }

        // Deterministic compatibility for classifier smoke coverage.
        public bool ShouldInspect(OcrRegionType regionType, long frameNumber) =>
            _trackers[regionType].ShouldInspect(frameNumber);

        // Deterministic compatibility for classifier smoke coverage.
        public void Observe(OcrRegionType regionType, bool present)
        {
            lock (_lock)
                _trackers[regionType].Observe(present);
        }

        public bool HasConfirmedPresence()
        {
            lock (_lock)
            {
                return _trackers.Values
                    .Select(tracker => tracker.Snapshot())
                    .Any(snapshot =>
                        snapshot.CurrentlyPresent &&
                        snapshot.Confirmed);
            }
        }

        public CollectionConfirmationDecision NextDecision()
        {
            lock (_lock)
            {
                var moonGet = Snapshot(OcrRegionType.MoonGet);
                var storyMoon = Snapshot(OcrRegionType.StoryMoon);
                CleanupReleasedEvent(moonGet, storyMoon);

                if (_event == null)
                    _event = StartEvent(moonGet, storyMoon);
                else
                    LinkNewGeneration(_event, moonGet, storyMoon);

                if (_event == null ||
                    _event.Resolved ||
                    _event.Saturated ||
                    _event.Pending != null)
                {
                    return default;
                }

                return _event.AttemptCount == 0
                    ? InitialDecision(_event)
                    : RetryDecision(_event);
            }
        }

        public bool RecordEnqueued(CollectionConfirmationDecision decision)
        {
            lock (_lock)
            {
                if (!decision.ShouldEnqueue ||
                    _event == null ||
                    _event.Pending != null ||
                    _event.AttemptCount + 1 != decision.EventAttempt)
                {
                    return false;
                }

                if (!_event.Generations.TryGetValue(
                        decision.RegionType,
                        out var generation) ||
                    generation != decision.Generation)
                {
                    return false;
                }

                var tracker = _trackers[decision.RegionType];
                if (!tracker.RecordEnqueued(
                        decision.Generation,
                        decision.RegionAttempt))
                {
                    return false;
                }

                _event.AttemptCount = decision.EventAttempt;
                _event.Pending = new ConfirmationKey(
                    decision.RegionType,
                    decision.Generation);
                _event.LastAttemptUnresolved = false;
                return true;
            }
        }

        public void RecordOutcome(
            OcrRegionType regionType,
            long generation,
            bool resolved)
        {
            lock (_lock)
            {
                if (!_trackers.TryGetValue(regionType, out var tracker))
                    return;

                tracker.RecordOutcome(generation, resolved);
                if (_event == null ||
                    !_event.Generations.TryGetValue(regionType, out var eventGeneration) ||
                    eventGeneration != generation)
                {
                    return;
                }

                var key = new ConfirmationKey(regionType, generation);
                if (_event.Pending == key)
                    _event.Pending = null;

                if (resolved)
                {
                    _event.Resolved = true;
                    _event.LastAttemptUnresolved = false;
                    SuppressOtherGenerations(_event, key);
                    return;
                }

                _event.LastAttemptUnresolved = true;
                if (_event.AttemptCount >= 2)
                {
                    _event.Saturated = true;
                    FinalizeGenerations(_event);
                    return;
                }

                _event.RetryBaselines.Clear();
                foreach (var pair in _event.Generations)
                {
                    var snapshot = Snapshot(pair.Key);
                    if (snapshot.Active && snapshot.Generation == pair.Value)
                    {
                        _event.RetryBaselines[
                            new ConfirmationKey(pair.Key, pair.Value)] =
                            new RetryBaseline(
                                snapshot.PresentObservationCount,
                                snapshot.LastObservation);
                    }
                }
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                foreach (var tracker in _trackers.Values)
                    tracker.Reset();

                _event = null;
            }
        }

        private CollectionConfirmationDecision InitialDecision(CollectionEvent activeEvent)
        {
            var primary = Snapshot(activeEvent.Primary);
            if (Matches(activeEvent, primary) &&
                primary.CurrentlyPresent &&
                primary.CanEnqueueInitial)
            {
                return Decision(primary, eventAttempt: 1);
            }

            if (activeEvent.Fallback is not { } fallbackType)
                return default;

            var fallback = Snapshot(fallbackType);
            if (!Matches(activeEvent, fallback) ||
                !fallback.CurrentlyPresent ||
                !fallback.CanEnqueueInitial)
            {
                return default;
            }

            if (Matches(activeEvent, primary) &&
                primary.CurrentlyPresent)
            {
                return default;
            }

            activeEvent.Primary = fallbackType;
            activeEvent.Fallback = null;
            return Decision(fallback, eventAttempt: 1);
        }

        private CollectionConfirmationDecision RetryDecision(CollectionEvent activeEvent)
        {
            if (!activeEvent.LastAttemptUnresolved)
                return default;

            if (activeEvent.Fallback is { } fallbackType)
            {
                var fallback = Snapshot(fallbackType);
                if (RetryReady(activeEvent, fallback))
                    return Decision(fallback, eventAttempt: 2);

                if (Matches(activeEvent, fallback) &&
                    fallback.CurrentlyPresent)
                {
                    return default;
                }
            }

            var primary = Snapshot(activeEvent.Primary);
            if (!Matches(activeEvent, primary) ||
                !primary.CurrentlyPresent)
            {
                return default;
            }

            var profile = CollectionConfirmationProfile.For(primary.RegionType);
            return primary.CanEnqueueRetry(profile)
                ? Decision(primary, eventAttempt: 2)
                : default;
        }

        private bool RetryReady(
            CollectionEvent activeEvent,
            CollectionConfirmationSnapshot snapshot)
        {
            if (!Matches(activeEvent, snapshot) ||
                !snapshot.CurrentlyPresent ||
                !snapshot.Confirmed ||
                snapshot.OcrPending ||
                snapshot.AttemptCount != 0)
            {
                return false;
            }

            var key = new ConfirmationKey(
                snapshot.RegionType,
                snapshot.Generation);
            if (!activeEvent.RetryBaselines.TryGetValue(key, out var baseline))
                return false;

            var profile = CollectionConfirmationProfile.For(snapshot.RegionType);
            return snapshot.PresentObservationCount > baseline.ObservationCount &&
                snapshot.LastObservation >= baseline.Timestamp &&
                CaptureTiming.HasElapsed(
                    snapshot.LastObservation - baseline.Timestamp,
                    profile.RetryPresentDuration);
        }

        private static CollectionConfirmationDecision Decision(
            CollectionConfirmationSnapshot snapshot,
            int eventAttempt)
        {
            return new CollectionConfirmationDecision(
                true,
                snapshot.RegionType,
                snapshot.Generation,
                snapshot.AttemptCount + 1,
                eventAttempt);
        }

        private CollectionEvent? StartEvent(
            CollectionConfirmationSnapshot moonGet,
            CollectionConfirmationSnapshot storyMoon)
        {
            if (moonGet.Active &&
                moonGet.CurrentlyPresent &&
                storyMoon.Active &&
                storyMoon.CurrentlyPresent)
            {
                return CollectionEvent.Overlap(storyMoon, moonGet);
            }

            if (storyMoon.Active && storyMoon.CurrentlyPresent)
                return CollectionEvent.Single(storyMoon);

            if (moonGet.Active && moonGet.CurrentlyPresent)
                return CollectionEvent.Single(moonGet);

            return null;
        }

        private void LinkNewGeneration(
            CollectionEvent activeEvent,
            CollectionConfirmationSnapshot moonGet,
            CollectionConfirmationSnapshot storyMoon)
        {
            LinkNewGeneration(activeEvent, moonGet);
            LinkNewGeneration(activeEvent, storyMoon);

            if (activeEvent.AttemptCount == 0 &&
                activeEvent.Generations.Count == 2)
            {
                activeEvent.Primary = OcrRegionType.StoryMoon;
                activeEvent.Fallback = OcrRegionType.MoonGet;
            }
        }

        private void LinkNewGeneration(
            CollectionEvent activeEvent,
            CollectionConfirmationSnapshot snapshot)
        {
            if (!snapshot.Active || !snapshot.CurrentlyPresent)
                return;

            if (activeEvent.Generations.TryGetValue(
                    snapshot.RegionType,
                    out var existingGeneration) &&
                existingGeneration == snapshot.Generation)
            {
                return;
            }

            if (activeEvent.Resolved || activeEvent.Saturated)
            {
                _trackers[snapshot.RegionType].Suppress(snapshot.Generation);
                activeEvent.Generations[snapshot.RegionType] =
                    snapshot.Generation;
                return;
            }

            activeEvent.Generations[snapshot.RegionType] =
                snapshot.Generation;
            if (snapshot.RegionType != activeEvent.Primary)
                activeEvent.Fallback = snapshot.RegionType;
        }

        private void CleanupReleasedEvent(
            CollectionConfirmationSnapshot moonGet,
            CollectionConfirmationSnapshot storyMoon)
        {
            if (_event == null)
                return;

            var anyActive = _event.Generations.Any(pair =>
            {
                var snapshot = pair.Key == OcrRegionType.MoonGet
                    ? moonGet
                    : storyMoon;
                return snapshot.Active &&
                    snapshot.Generation == pair.Value;
            });

            if (!anyActive)
                _event = null;
        }

        private void SuppressOtherGenerations(
            CollectionEvent activeEvent,
            ConfirmationKey resolved)
        {
            foreach (var pair in activeEvent.Generations)
            {
                var key = new ConfirmationKey(pair.Key, pair.Value);
                if (key != resolved)
                    _trackers[pair.Key].Suppress(pair.Value);
            }
        }

        private void FinalizeGenerations(CollectionEvent activeEvent)
        {
            foreach (var pair in activeEvent.Generations)
                _trackers[pair.Key].FinalizeUnresolved(pair.Value);
        }

        private CollectionConfirmationSnapshot Snapshot(OcrRegionType regionType)
        {
            return _trackers[regionType].Snapshot();
        }

        private static bool Matches(
            CollectionEvent activeEvent,
            CollectionConfirmationSnapshot snapshot)
        {
            return activeEvent.Generations.TryGetValue(
                    snapshot.RegionType,
                    out var generation) &&
                generation == snapshot.Generation &&
                snapshot.Active;
        }

        private sealed class CollectionEvent
        {
            public Dictionary<OcrRegionType, long> Generations { get; } = new();
            public Dictionary<ConfirmationKey, RetryBaseline> RetryBaselines { get; } = new();
            public OcrRegionType Primary { get; set; }
            public OcrRegionType? Fallback { get; set; }
            public int AttemptCount { get; set; }
            public ConfirmationKey? Pending { get; set; }
            public bool LastAttemptUnresolved { get; set; }
            public bool Resolved { get; set; }
            public bool Saturated { get; set; }

            public static CollectionEvent Single(
                CollectionConfirmationSnapshot primary)
            {
                var activeEvent = new CollectionEvent
                {
                    Primary = primary.RegionType
                };
                activeEvent.Generations[primary.RegionType] =
                    primary.Generation;
                return activeEvent;
            }

            public static CollectionEvent Overlap(
                CollectionConfirmationSnapshot primary,
                CollectionConfirmationSnapshot fallback)
            {
                var activeEvent = Single(primary);
                activeEvent.Fallback = fallback.RegionType;
                activeEvent.Generations[fallback.RegionType] =
                    fallback.Generation;
                return activeEvent;
            }
        }
    }

    internal readonly record struct CollectionConfirmationDecision(
        bool ShouldEnqueue,
        OcrRegionType RegionType,
        long Generation,
        int RegionAttempt,
        int EventAttempt);

    internal readonly record struct ConfirmationKey(
        OcrRegionType RegionType,
        long Generation);

    internal readonly record struct RetryBaseline(
        int ObservationCount,
        DateTime Timestamp);
}
