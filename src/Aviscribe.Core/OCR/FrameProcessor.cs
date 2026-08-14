using OpenCvSharp;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using Aviscribe.Core.KingdomDetection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Aviscribe.Core.Ocr
{
    public sealed class FrameProcessor : IDisposable
    {
        private readonly IOcrService _ocr;
        private readonly MoonMatcher _matcher;
        private readonly GameState _state;
        private readonly IAppDiagnostics _diagnostics;

        private readonly object _lock = new();
        private readonly object _lifecycleLock = new();
        private VideoFrame? _latestFrame;

        private CancellationTokenSource? _cts;
        private Task? _frameWorker;
        private Task? _ocrWorker;
        private volatile bool _acceptingFrames;
        private bool _disposed;

        private readonly TalkatooConfirmationTracker _talkatooConfirmation = new();
        private readonly TalkatooAdaptiveAnalyzer _talkatooAdaptiveAnalyzer = new();
        private readonly CollectionConfirmationCoordinator _collectionConfirmation = new();
        private long _processedFrameCount;
        private string _observedKingdom;
        private long _suppressTalkatooUntilFrame;
        private const double WeakCollectionMatchThreshold = 0.50;
        private const double WeakStoryMoonMatchThreshold = 0.40;
        private const double WeakCollectionPendingMargin = 0.08;

        private readonly object _ocrQueueLock = new();
        private readonly Queue<OcrWorkItem> _ocrQueue = new();
        private readonly HashSet<ConfirmationKey> _queuedOrInFlightConfirmations = new();
        private readonly ITextPresenceDetector _textDetector;
        private readonly IKingdomDetector? _kingdomDetector;
        private readonly KingdomDetectionTracker _kingdomDetectionTracker = new();
        private CaptureCropSettings _cropSettings;

        private readonly OcrRegion[] _regions;

        public event EventHandler<AmbiguousOcrResult>? AmbiguousMatchReceived;

        public FrameProcessor(
            IOcrService ocr,
            MoonMatcher matcher,
            GameState state,
            ITextPresenceDetector? textDetector = null,
            CaptureCropSettings? cropSettings = null,
            IAppDiagnostics? diagnostics = null,
            IKingdomDetector? kingdomDetector = null)
        {
            _ocr = ocr;
            _matcher = matcher;
            _state = state;
            _diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
            _textDetector = textDetector ?? new HeuristicTextPresenceDetector();
            _kingdomDetector = kingdomDetector;
            _cropSettings = (cropSettings ?? CaptureCropSettings.Default).Clone();
            _observedKingdom = state.CurrentKingdom;

            _regions =
            [
                //new(OcrRegionType.Talkatoo, new Rect(666, 828, 649, 113), _textDetector), // multiline
                new(
                    OcrRegionType.Talkatoo,
                    OcrReferenceLayout.Talkatoo.OcrBounds,
                    _textDetector,
                    StableFrameCount: TalkatooConfirmationTracker.RequiredStableFrames,
                    StableImageMaxHammingDistance: 64,
                    DetectionBounds: OcrReferenceLayout.Talkatoo.DetectionBounds,
                    DetectionIntervalFrames: TalkatooConfirmationTracker.IdleDetectionIntervalFrames), // single line
                new(
                    OcrRegionType.MoonGet,
                    CollectionConfirmationProfile.MoonGet.OcrBounds,
                    _textDetector,
                    StableFrameCount: CollectionConfirmationProfile.MoonGet.RequiredPresentObservations,
                    DetectionBounds: CollectionConfirmationProfile.MoonGet.DetectionBounds,
                    DetectionIntervalFrames: CollectionConfirmationProfile.MoonGet.DetectionIntervalFrames),
                new(
                    OcrRegionType.StoryMoon,
                    CollectionConfirmationProfile.StoryMoon.OcrBounds,
                    _textDetector,
                    StableFrameCount: CollectionConfirmationProfile.StoryMoon.RequiredPresentObservations,
                    DetectionBounds: CollectionConfirmationProfile.StoryMoon.DetectionBounds,
                    DetectionIntervalFrames: CollectionConfirmationProfile.StoryMoon.DetectionIntervalFrames)
            ];
        }

        // ----------------------------
        // LIFECYCLE
        // ----------------------------
        public void Start()
        {
            lock (_lifecycleLock)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (_cts != null)
                    return;

                _cts = new CancellationTokenSource();
                _acceptingFrames = true;
                var token = _cts.Token;
                _frameWorker = Task.Run(() => FrameLoop(token), token);
                _ocrWorker = Task.Run(() => OcrLoop(token), token);
            }
        }

        public void Stop()
        {
            CancellationTokenSource? cancellation;
            Task[] workers;
            lock (_lifecycleLock)
            {
                if (_cts == null)
                    return;

                _acceptingFrames = false;
                cancellation = _cts;
                _cts = null;
                workers = new[] { _frameWorker, _ocrWorker }
                    .Where(worker => worker != null)
                    .Cast<Task>()
                    .ToArray();
                _frameWorker = null;
                _ocrWorker = null;
            }

            cancellation.Cancel();
            try
            {
                Task.WaitAll(workers);
            }
            catch (AggregateException ex)
                when (ex.InnerExceptions.All(item =>
                    item is OperationCanceledException))
            {
            }
            finally
            {
                cancellation.Dispose();
            }

            lock (_lock)
            {
                _latestFrame?.Dispose();
                _latestFrame = null;
            }
            ClearOcrQueue();
        }

        // ----------------------------
        // FRAME INPUT
        // ----------------------------
        public void PushFrame(VideoFrame frame)
        {
            ArgumentNullException.ThrowIfNull(frame);
            lock (_lock)
            {
                if (!_acceptingFrames)
                {
                    frame.Dispose();
                    return;
                }

                _latestFrame?.Dispose();
                _latestFrame = frame;
            }
        }

        public void UpdateCrop(CaptureCropSettings cropSettings)
        {
            ArgumentNullException.ThrowIfNull(cropSettings);
            Volatile.Write(ref _cropSettings, cropSettings.Clone());
        }

        // ----------------------------
        // FRAME LOOP (FAST)
        // ----------------------------
        private void FrameLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                VideoFrame? frame = null;

                lock (_lock)
                {
                    frame = _latestFrame;
                    _latestFrame = null;
                }

                if (frame == null)
                {
                    Thread.Sleep(1);
                    continue;
                }

                try
                {
                    ProcessFrame(frame);
                }
                finally
                {
                    frame.Dispose();
                }
            }
        }

        private void ProcessFrame(VideoFrame frame)
        {
            var source = frame.Frame;
            if (source.Empty())
                return;

            var cropSettings = Volatile.Read(ref _cropSettings);
            if (GameplayFrameNormalizer.IsAlreadyNormalized(
                    source,
                    cropSettings))
            {
                ProcessReferenceFrame(source, frame.Timestamp);
                return;
            }

            using var normalized = GameplayFrameNormalizer.Normalize(
                source,
                cropSettings);
            ProcessReferenceFrame(normalized, frame.Timestamp);
        }

        private void ProcessReferenceFrame(Mat mat, DateTime timestamp)
        {
            _processedFrameCount++;
            ResetRegionStateIfKingdomChanged();
            InspectKingdom(mat, timestamp);
            ResetRegionStateIfKingdomChanged();

            var kingdom = _state.CurrentKingdom;
            var settings = _state.Settings.Clone();

            OcrRegion? talkatooRegion = null;
            TalkatooConfirmationDecision talkatooDecision = default;

            foreach (var region in _regions)
            {
                if (region.Type == OcrRegionType.Talkatoo)
                {
                    if (!_talkatooConfirmation.ShouldInspect(_processedFrameCount))
                        continue;

                    using var talkatooCrop = mat[region.DetectionBounds ?? region.Bounds];
                    var useAdaptiveDetection =
                        settings.AdaptiveTalkatooDetection &&
                        region.Detector.GetType() == typeof(HeuristicTextPresenceDetector);
                    bool talkatooPresent;
                    TalkatooPromptSignature? signature;

                    if (useAdaptiveDetection)
                    {
                        var analysis = _talkatooAdaptiveAnalyzer.Analyze(talkatooCrop);
                        talkatooPresent = analysis.Present;
                        signature = analysis.Present
                            ? analysis.Adapted
                                ? TalkatooPromptSignature.CaptureAdaptive(
                                    talkatooCrop,
                                    analysis)
                                : TalkatooPromptSignature.Capture(talkatooCrop)
                            : null;

                        if (analysis.StartedAdaptiveRun)
                        {
                            _diagnostics.Debug(
                                $"Adaptive Talkatoo detection selected " +
                                $"{analysis.Gain:0.00}x gain.");
                        }
                    }
                    else
                    {
                        _talkatooAdaptiveAnalyzer.Reset();
                        var talkatooDetection = region.Detector.Detect(
                            region.Type,
                            talkatooCrop);
                        talkatooPresent = talkatooDetection.Present;
                        signature = talkatooDetection.Present
                            ? TalkatooPromptSignature.Capture(talkatooCrop)
                            : null;
                    }

                    var decision = _talkatooConfirmation.Observe(
                        talkatooPresent,
                        signature);
                    talkatooRegion = region;
                    talkatooDecision = decision;
                    continue;
                }

                if (!_collectionConfirmation.ShouldInspect(
                        region.Type,
                        _processedFrameCount))
                {
                    continue;
                }

                using var detectionCrop = mat[region.DetectionBounds ?? region.Bounds];
                var detection = region.Detector.Detect(region.Type, detectionCrop);
                _collectionConfirmation.Observe(region.Type, detection.Present);
            }

            var collectionDecision = _collectionConfirmation.NextDecision();
            if (_collectionConfirmation.HasConfirmedPresence())
                _suppressTalkatooUntilFrame = _processedFrameCount + 20;
            var suppressTalkatoo = _processedFrameCount <= _suppressTalkatooUntilFrame;

            if (collectionDecision.ShouldEnqueue)
            {
                var region = _regions.First(item =>
                    item.Type == collectionDecision.RegionType);
                using var ocrCrop = mat[region.Bounds];
                var hash = ImageHash.Compute(ocrCrop);
                if (_collectionConfirmation.RecordEnqueued(collectionDecision))
                {
                    if (EnqueueOcr(
                        region.Type,
                        ocrCrop.Clone(),
                        kingdom,
                        settings,
                        hash,
                        _processedFrameCount,
                        collectionDecision.Generation))
                    {
                        _diagnostics.Debug(
                            $"ENQUEUE OCR ({region.Type}, attempt " +
                            $"{collectionDecision.EventAttempt})");
                    }
                    else
                    {
                        _collectionConfirmation.RecordOutcome(
                            region.Type,
                            collectionDecision.Generation,
                            resolved: false);
                    }
                }
            }

            if (talkatooRegion != null &&
                talkatooDecision.ShouldEnqueue &&
                !suppressTalkatoo)
            {
                using var ocrCrop = mat[talkatooRegion.Bounds];
                var hash = ImageHash.Compute(ocrCrop);
                if (_talkatooConfirmation.RecordEnqueued(
                    talkatooDecision.Generation,
                    talkatooDecision.Attempt))
                {
                    if (EnqueueOcr(
                        talkatooRegion.Type,
                        ocrCrop.Clone(),
                        kingdom,
                        settings,
                        hash,
                        _processedFrameCount,
                        talkatooDecision.Generation))
                    {
                        _diagnostics.Debug(
                            $"ENQUEUE OCR ({talkatooRegion.Type}, attempt " +
                            $"{talkatooDecision.Attempt})");
                    }
                    else
                    {
                        _talkatooConfirmation.RecordUnresolved(
                            talkatooDecision.Generation);
                    }
                }
            }
        }

        // ----------------------------
        // OCR LOOP (SLOW)
        // ----------------------------
        private void OcrLoop(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!TryDequeueOcr(out var item))
                {
                    Thread.Sleep(5);
                    continue;
                }

                try
                {
                    var text = _ocr.ReadText(item.Image);

                    _diagnostics.Debug(
                        $"OCR RESULT ({item.Type}): \"{text}\"");

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        RecordConfirmationOutcome(item, resolved: false);
                        continue;
                    }

                    if (!string.Equals(item.Kingdom, _state.CurrentKingdom, StringComparison.OrdinalIgnoreCase))
                    {
                        _diagnostics.Debug(
                            $"SKIP OCR ({item.Type}): Kingdom changed from " +
                            $"{item.Kingdom} to {_state.CurrentKingdom}");
                        RecordConfirmationOutcome(item, resolved: false);
                        continue;
                    }

                    var result = item.Type == OcrRegionType.Talkatoo
                        ? _matcher.MatchTalkatooText(text, item.Kingdom, item.Settings)
                        : _matcher.MatchCollectionText(text, item.Kingdom, item.Settings);

                    if (result.IsAmbiguous)
                    {
                        if (TryResolveAmbiguousMatch(item.Type, result, out var resolvedMatch))
                        {
                            _diagnostics.Debug(
                                $"RESOLVED AMBIGUOUS OCR ({item.Type}): " +
                                $"\"{text}\" -> {resolvedMatch.English}");
                            RecordConfirmationOutcome(item, resolved: true);
                            Handle(item.Type, resolvedMatch);
                            continue;
                        }

                        _diagnostics.Debug(
                            $"AMBIGUOUS OCR ({item.Type}): \"{text}\"");
                        RecordConfirmationOutcome(item, resolved: false);
                        AmbiguousMatchReceived?.Invoke(
                            this,
                            new AmbiguousOcrResult(item.Type, text, result.Candidates));
                        continue;
                    }

                    if (result.BestMatch != null)
                    {
                        RecordConfirmationOutcome(item, resolved: true);
                        Handle(item.Type, result.BestMatch);
                        continue;
                    }

                    if (TryResolveWeakCollectionMatch(item.Type, result, out var weakResolvedMatch))
                    {
                        _diagnostics.Debug(
                            $"RESOLVED WEAK OCR ({item.Type}): " +
                            $"\"{text}\" -> {weakResolvedMatch.English}");
                        RecordConfirmationOutcome(item, resolved: true);
                        Handle(item.Type, weakResolvedMatch);
                        continue;
                    }

                    RecordConfirmationOutcome(item, resolved: false);
                }
                catch (Exception ex)
                {
                    RecordConfirmationOutcome(item, resolved: false);
                    _diagnostics.Error(
                        $"OCR failed for {item.Type}.",
                        ex);
                }
                finally
                {
                    CompleteConfirmation(item);
                    item.Image.Dispose();
                }
            }
        }

        private bool TryResolveAmbiguousMatch(OcrRegionType type, MatchResult result, out Moon match)
        {
            var snapshot = _state.CreateSnapshot();
            var candidates = result.Candidates
                .Where(candidate => candidate.score >= _matcher.Threshold)
                .Select(candidate => candidate.moon)
                .Distinct()
                .ToList();

            var resolved = type == OcrRegionType.Talkatoo
                ? ResolveAmbiguousTalkatoo(candidates, snapshot)
                : ResolveAmbiguousCollection(candidates, snapshot);

            match = resolved!;
            return resolved != null;
        }

        private bool TryResolveWeakCollectionMatch(OcrRegionType type, MatchResult result, out Moon match)
        {
            match = null!;

            if (type == OcrRegionType.Talkatoo)
                return false;

            var threshold = type == OcrRegionType.StoryMoon
                ? WeakStoryMoonMatchThreshold
                : WeakCollectionMatchThreshold;
            if (result.Score < threshold)
                return false;

            var snapshot = _state.CreateSnapshot();
            var topScore = result.Candidates.Count == 0
                ? 0
                : result.Candidates.Max(candidate => candidate.score);
            var weakCandidates = result.Candidates
                .Where(candidate =>
                    candidate.score >= threshold &&
                    topScore - candidate.score <= WeakCollectionPendingMargin)
                .Select(candidate => candidate.moon)
                .Distinct()
                .ToList();

            var pending = weakCandidates
                .Where(candidate => snapshot.Pending.Any(moon => moon.Id == candidate.Id))
                .ToList();

            if (pending.Count != 1)
            {
                if (type != OcrRegionType.StoryMoon)
                    return false;

                var story = weakCandidates
                    .Where(candidate =>
                        candidate.IsStory &&
                        !snapshot.Collected.Any(moon => moon.Id == candidate.Id) &&
                        !snapshot.UncountedCollected.Any(moon => moon.Id == candidate.Id))
                    .ToList();
                if (story.Count != 1)
                    return false;

                match = story[0];
                return true;
            }

            match = pending[0];
            return true;
        }

        private static Moon? ResolveAmbiguousTalkatoo(
            IReadOnlyList<Moon> candidates,
            GameStateSnapshot snapshot)
        {
            var eligible = candidates
                .Where(candidate =>
                    !snapshot.Pending.Any(moon => moon.Id == candidate.Id) &&
                    !snapshot.Collected.Any(moon => moon.Id == candidate.Id) &&
                    !snapshot.UncountedCollected.Any(moon => moon.Id == candidate.Id))
                .ToList();

            return eligible.Count == 1 ? eligible[0] : null;
        }

        private static Moon? ResolveAmbiguousCollection(
            IReadOnlyList<Moon> candidates,
            GameStateSnapshot snapshot)
        {
            var pending = candidates
                .Where(candidate => snapshot.Pending.Any(moon => moon.Id == candidate.Id))
                .ToList();

            if (pending.Count == 1)
                return pending[0];

            var story = candidates
                .Where(candidate =>
                    candidate.IsStory &&
                    !snapshot.Collected.Any(moon => moon.Id == candidate.Id) &&
                    !snapshot.UncountedCollected.Any(moon => moon.Id == candidate.Id))
                .ToList();

            return story.Count == 1 ? story[0] : null;
        }

        private bool EnqueueOcr(
            OcrRegionType type,
            Mat image,
            string kingdom,
            RunSettings settings,
            ulong imageHash,
            long frameIndex,
            long confirmationGeneration = 0)
        {
            var maxQueuedPerRegion = type == OcrRegionType.Talkatoo ? 8 : 2;
            OcrWorkItem? droppedItem = null;

            lock (_ocrQueueLock)
            {
                var confirmationKey = new ConfirmationKey(
                    type,
                    confirmationGeneration);
                if (confirmationGeneration != 0 &&
                    _queuedOrInFlightConfirmations.Contains(confirmationKey))
                {
                    image.Dispose();
                    return false;
                }

                if (_ocrQueue.Count(item => item.Type == type) >= maxQueuedPerRegion)
                {
                    var kept = new Queue<OcrWorkItem>(_ocrQueue.Count);
                    var droppedOne = false;

                    while (_ocrQueue.Count > 0)
                    {
                        var item = _ocrQueue.Dequeue();
                        if (!droppedOne && item.Type == type)
                        {
                            droppedItem = item;
                            RemoveConfirmationReservationCore(item);
                            droppedOne = true;
                            continue;
                        }

                        kept.Enqueue(item);
                    }

                    while (kept.Count > 0)
                        _ocrQueue.Enqueue(kept.Dequeue());
                }

                var workItem = new OcrWorkItem(
                    type,
                    image,
                    kingdom,
                    settings.Clone(),
                    imageHash,
                    frameIndex,
                    confirmationGeneration);
                if (confirmationGeneration != 0)
                    _queuedOrInFlightConfirmations.Add(confirmationKey);

                if (type == OcrRegionType.Talkatoo)
                {
                    _ocrQueue.Enqueue(workItem);
                }
                else
                {
                    var prioritized = new Queue<OcrWorkItem>(_ocrQueue.Count + 1);
                    var delayedTalkatoo = new Queue<OcrWorkItem>();

                    while (_ocrQueue.Count > 0)
                    {
                        var item = _ocrQueue.Dequeue();
                        if (item.Type == OcrRegionType.Talkatoo)
                            delayedTalkatoo.Enqueue(item);
                        else
                            prioritized.Enqueue(item);
                    }

                    prioritized.Enqueue(workItem);

                    while (delayedTalkatoo.Count > 0)
                        prioritized.Enqueue(delayedTalkatoo.Dequeue());

                    while (prioritized.Count > 0)
                        _ocrQueue.Enqueue(prioritized.Dequeue());
                }
            }

            if (droppedItem is { } dropped)
            {
                RecordConfirmationOutcome(dropped, resolved: false);
                dropped.Image.Dispose();
            }

            return true;
        }

        private bool TryDequeueOcr(out OcrWorkItem item)
        {
            lock (_ocrQueueLock)
            {
                if (_ocrQueue.Count > 0)
                {
                    item = _ocrQueue.Dequeue();
                    return true;
                }
            }

            item = default;
            return false;
        }

        private void CompleteConfirmation(OcrWorkItem item)
        {
            lock (_ocrQueueLock)
                RemoveConfirmationReservationCore(item);
        }

        private void ClearOcrQueue(bool recordUnresolved = true)
        {
            var droppedItems = new List<OcrWorkItem>();
            lock (_ocrQueueLock)
            {
                while (_ocrQueue.Count > 0)
                {
                    var item = _ocrQueue.Dequeue();
                    RemoveConfirmationReservationCore(item);
                    droppedItems.Add(item);
                }
            }

            foreach (var item in droppedItems)
            {
                if (recordUnresolved)
                    RecordConfirmationOutcome(item, resolved: false);

                item.Image.Dispose();
            }
        }

        private void RemoveConfirmationReservationCore(OcrWorkItem item)
        {
            if (item.ConfirmationGeneration == 0)
                return;

            _queuedOrInFlightConfirmations.Remove(new ConfirmationKey(
                item.Type,
                item.ConfirmationGeneration));
        }

        private void ResetRegionStateIfKingdomChanged()
        {
            var currentKingdom = _state.CurrentKingdom;
            if (string.Equals(currentKingdom, _observedKingdom, StringComparison.OrdinalIgnoreCase))
                return;

            ClearOcrQueue(recordUnresolved: true);
            _talkatooConfirmation.Reset();
            _collectionConfirmation.Reset();
            _kingdomDetectionTracker.ResetCandidate();
            _suppressTalkatooUntilFrame = 0;
            _observedKingdom = currentKingdom;
        }

        private void InspectKingdom(Mat mat, DateTime timestamp)
        {
            var settings = _state.Settings.Clone();
            if (!settings.AutomaticallySwitchKingdoms || _kingdomDetector == null)
            {
                _kingdomDetectionTracker.Reset();
                return;
            }

            if (!_kingdomDetectionTracker.ShouldInspect(timestamp))
                return;

            KingdomDetectionResult result;
            try
            {
                result = _kingdomDetector.Detect(mat);
            }
            catch (Exception ex)
            {
                _kingdomDetectionTracker.ResetCandidate();
                _diagnostics.Error(
                    "Automatic kingdom detection failed for a frame.",
                    ex);
                return;
            }

            if (result.IsMatch &&
                result.Kingdom != null &&
                !IsKingdomEnabled(result.Kingdom, settings))
            {
                _kingdomDetectionTracker.ResetCandidate();
                if (result.Score != 0)
                {
                    _diagnostics.Debug(
                        $"KINGDOM DETECTION ignored {result.Kingdom}; " +
                        "the kingdom is disabled by the current run settings.");
                }
                return;
            }

            if (result.Score != 0)
            {
                _diagnostics.Debug(
                    result.IsMatch
                        ? $"KINGDOM DETECTION {result.Kingdom}: " +
                          $"{result.Score:0.000} (margin " +
                          $"{result.Score - result.RunnerUpScore:0.000})"
                        : $"KINGDOM DETECTION {result.Status}: " +
                          $"{result.Score:0.000} (runner-up " +
                          $"{result.RunnerUpScore:0.000})");
            }

            var confirmed = _kingdomDetectionTracker.Observe(
                result,
                timestamp,
                _state.CurrentKingdom);
            if (confirmed == null)
                return;

            _state.SetKingdom(confirmed);
            _diagnostics.Information(
                $"Automatically switched to {confirmed} from the HUD symbol.");
        }

        private static bool IsKingdomEnabled(
            string kingdom,
            RunSettings settings)
        {
            return settings.IncludePostGameKingdoms ||
                KingdomRoute.GetRequirement(kingdom) > 0;
        }

        private void RecordConfirmationOutcome(OcrWorkItem item, bool resolved)
        {
            switch (item.Type)
            {
                case OcrRegionType.Talkatoo:
                    if (resolved)
                        _talkatooConfirmation.RecordResolved(item.ConfirmationGeneration);
                    else
                        _talkatooConfirmation.RecordUnresolved(item.ConfirmationGeneration);
                    break;

                case OcrRegionType.MoonGet:
                case OcrRegionType.StoryMoon:
                    _collectionConfirmation.RecordOutcome(
                        item.Type,
                        item.ConfirmationGeneration,
                        resolved);
                    break;
            }
        }

        // ----------------------------
        // RESULT HANDLING
        // ----------------------------
        private void Handle(OcrRegionType type, Moon match)
        {
            switch (type)
            {
                case OcrRegionType.Talkatoo:
                    if (_state.TryAddPending(match))
                        _diagnostics.Debug($"ADD: {match.English}");
                    break;

                case OcrRegionType.MoonGet:
                case OcrRegionType.StoryMoon:
                    var outcome = _state.MarkCollected(match);
                    switch (outcome)
                    {
                        case CollectionOutcome.Counted:
                            _diagnostics.Debug(
                                $"COLLECTED: {match.English} " +
                                $"({_state.CountedMoonCount} counted)");
                            break;

                        case CollectionOutcome.Uncounted:
                            _diagnostics.Debug(
                                $"UNCOUNTED: {match.English} " +
                                $"({_state.ActualMoonCount} actual, " +
                                $"{_state.CountedMoonCount} counted)");
                            break;

                        case CollectionOutcome.AlreadyCounted:
                            _diagnostics.Debug(
                                $"SKIP COLLECTED: {match.English}");
                            break;

                        case CollectionOutcome.AlreadyUncounted:
                            _diagnostics.Debug(
                                $"SKIP UNCOUNTED: {match.English}");
                            break;

                        default:
                            _diagnostics.Debug($"IGNORED: {match.English}");
                            break;
                    }
                    break;
            }
        }

        public void Dispose()
        {
            lock (_lifecycleLock)
            {
                if (_disposed)
                    return;
                _disposed = true;
            }

            Stop();
            if (_ocr is IDisposable disposableOcr)
                disposableOcr.Dispose();
            if (_kingdomDetector is IDisposable disposableKingdomDetector)
                disposableKingdomDetector.Dispose();
        }

        private readonly record struct OcrWorkItem(
            OcrRegionType Type,
            Mat Image,
            string Kingdom,
            RunSettings Settings,
            ulong ImageHash,
            long FrameIndex,
            long ConfirmationGeneration);
    }
}
