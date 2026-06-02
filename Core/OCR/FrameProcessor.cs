using OpenCvSharp;
using Aviscribe.Core.Capture;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Aviscribe.Core.Ocr
{
    public class FrameProcessor
    {
        private readonly IOcrService _ocr;
        private readonly MoonMatcher _matcher;
        private readonly GameState _state;

        private readonly object _lock = new();
        private VideoFrame? _latestFrame;

        private CancellationTokenSource? _cts;
        private Task? _frameWorker;
        private Task? _ocrWorker;

        private readonly Dictionary<OcrRegionType, RegionHistory> _history = new();
        private readonly HashSet<OcrRegionType> _activeRegions = new();
        private readonly Dictionary<OcrRegionType, ulong> _lastHashes = new();
        private long _processedFrameCount;
        private string _observedKingdom;
        private long _suppressStoryMoonUntilFrame;

        private readonly object _ocrQueueLock = new();
        private readonly Queue<OcrWorkItem> _ocrQueue = new();
        private readonly ITextPresenceDetector _textDetector;

        private readonly OcrRegion[] _regions;

        public event EventHandler<AmbiguousOcrResult>? AmbiguousMatchReceived;

        public FrameProcessor(
            IOcrService ocr,
            MoonMatcher matcher,
            GameState state,
            ITextPresenceDetector? textDetector = null)
        {
            _ocr = ocr;
            _matcher = matcher;
            _state = state;
            _textDetector = textDetector ?? new HeuristicTextPresenceDetector();
            _observedKingdom = state.CurrentKingdom;

            _regions =
            [
                //new(OcrRegionType.Talkatoo, new Rect(666, 828, 649, 113), _textDetector), // multiline
                new(OcrRegionType.Talkatoo, new Rect(666, 862, 649, 48), _textDetector, StableFrameCount: 2, StableImageMaxHammingDistance: 64), // single line
                new(
                    OcrRegionType.MoonGet,
                    new Rect(490, 797, 930, 60),
                    _textDetector,
                    StableFrameCount: 1,
                    StableImageMaxHammingDistance: 64,
                    DetectionBounds: new Rect(320, 600, 1250, 250),
                    DetectionIntervalFrames: 5),
                new(OcrRegionType.StoryMoon, new Rect(450, 820, 1100, 150), _textDetector, StableFrameCount: 2)
            ];
        }

        // ----------------------------
        // LIFECYCLE
        // ----------------------------
        public void Start()
        {
            _cts = new CancellationTokenSource();

            _frameWorker = Task.Run(FrameLoop);
            _ocrWorker = Task.Run(OcrLoop);
        }

        public void Stop()
        {
            if (_frameWorker == null && _ocrWorker == null) // never actually started
                return;

            _cts?.Cancel();
            var workers = new[] { _frameWorker, _ocrWorker }
                .Where(worker => worker != null)
                .Cast<Task>()
                .ToArray();

            Task.WaitAll(workers, 1500);
            ClearOcrQueue();
        }

        // ----------------------------
        // FRAME INPUT
        // ----------------------------
        public void PushFrame(VideoFrame frame)
        {
            lock (_lock)
            {
                _latestFrame?.Dispose();
                _latestFrame = frame;
            }
        }

        // ----------------------------
        // FRAME LOOP (FAST)
        // ----------------------------
        private void FrameLoop()
        {
            while (!_cts!.IsCancellationRequested)
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
            var mat = frame.Frame;

            if (mat.Empty())
                return;

            _processedFrameCount++;
            ResetRegionStateIfKingdomChanged();

            var kingdom = _state.CurrentKingdom;
            var settings = _state.Settings.Clone();

            var frameStates = new List<RegionFrameState>(_regions.Length);

            foreach (var region in _regions)
            {
                if ((_processedFrameCount - 1) % Math.Max(1, region.DetectionIntervalFrames) != 0)
                    continue;

                using var detectionCrop = mat[region.DetectionBounds ?? region.Bounds];

                var detection = region.Detector.Detect(region.Type, detectionCrop);
                bool detected = detection.Present;
                //Console.WriteLine($"DETECTION ({region.Type}): {detected}");

                if (!_history.TryGetValue(region.Type, out var history))
                {
                    history = new RegionHistory(region.StableFrameCount);
                    _history[region.Type] = history;
                }

                history.Add(detected, detectionCrop);

                bool stable = history.IsStableDetection() &&
                              history.IsStableImage(region.StableImageMaxHammingDistance);

                //Console.WriteLine($"STABILITY ({region.Type}): {stable}");

                frameStates.Add(new RegionFrameState(region, detected, stable));
            }

            var storyMoonDetected = frameStates.Any(state =>
                state.Region.Type == OcrRegionType.StoryMoon &&
                state.Detected);
            var storyMoonStable = frameStates.Any(state =>
                state.Region.Type == OcrRegionType.StoryMoon &&
                state.Stable);
            var moonGetStable = frameStates.Any(state =>
                state.Region.Type == OcrRegionType.MoonGet &&
                state.Stable);

            if (moonGetStable && !storyMoonDetected)
            {
                _suppressStoryMoonUntilFrame = _processedFrameCount + 120;
            }

            var suppressStoryMoon = !storyMoonStable &&
                _processedFrameCount <= _suppressStoryMoonUntilFrame;

            foreach (var state in frameStates)
            {
                var region = state.Region;
                var stable = state.Stable &&
                    !(region.Type == OcrRegionType.StoryMoon && suppressStoryMoon);

                if (stable)
                {
                    if (!_activeRegions.Contains(region.Type))
                    {
                        using var ocrCrop = mat[region.Bounds];
                        ulong hash = ImageHash.Compute(ocrCrop);
                        var useHashDedupe = region.Type == OcrRegionType.Talkatoo;

                        if (!useHashDedupe ||
                            !_lastHashes.TryGetValue(region.Type, out var lastHash) ||
                            ImageHash.Hamming(hash, lastHash) > 5)
                        {
                            EnqueueOcr(region.Type, ocrCrop.Clone(), kingdom, settings);

                            if (useHashDedupe)
                                _lastHashes[region.Type] = hash;

                            Console.WriteLine($"ENQUEUE OCR ({region.Type})");
                        }
                        else
                        {
                            Console.WriteLine(hash == lastHash
                                ? $"SKIP OCR ({region.Type}): Image unchanged"
                                : $"SKIP OCR ({region.Type}): Image similar (Hamming {ImageHash.Hamming(hash, lastHash)})");
                        }

                        _activeRegions.Add(region.Type);
                    }
                }
                else
                {
                    _activeRegions.Remove(region.Type);
                }
            }
        }

        private readonly record struct RegionFrameState(OcrRegion Region, bool Detected, bool Stable);

        // ----------------------------
        // OCR LOOP (SLOW)
        // ----------------------------
        private void OcrLoop()
        {
            while (!_cts!.IsCancellationRequested)
            {
                if (!TryDequeueOcr(out var item))
                {
                    Thread.Sleep(5);
                    continue;
                }

                try
                {
                    var text = _ocr.ReadText(item.Image);

                    //Console.WriteLine($"OCR RESULT ({item.Type}): \"{text}\"");

                    if (string.IsNullOrWhiteSpace(text))
                        continue;

                    if (!string.Equals(item.Kingdom, _state.CurrentKingdom, StringComparison.OrdinalIgnoreCase))
                    {
                        Console.WriteLine($"SKIP OCR ({item.Type}): Kingdom changed from {item.Kingdom} to {_state.CurrentKingdom}");
                        continue;
                    }

                    var result = item.Type == OcrRegionType.Talkatoo
                        ? _matcher.MatchTalkatooText(text, item.Kingdom, item.Settings)
                        : _matcher.MatchCollectionText(text, item.Kingdom, item.Settings);

                    if (result.IsAmbiguous)
                    {
                        if (TryResolveAmbiguousMatch(item.Type, result, out var resolvedMatch))
                        {
                            Console.WriteLine($"RESOLVED AMBIGUOUS OCR ({item.Type}): \"{text}\" -> {resolvedMatch.English}");
                            Handle(item.Type, resolvedMatch);
                            continue;
                        }

                        Console.WriteLine($"AMBIGUOUS OCR ({item.Type}): \"{text}\"");
                        AmbiguousMatchReceived?.Invoke(
                            this,
                            new AmbiguousOcrResult(item.Type, text, result.Candidates));
                        continue;
                    }

                    if (result.BestMatch != null)
                    {
                        Handle(item.Type, result.BestMatch);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"OCR ERROR: {ex.Message}");
                }
                finally
                {
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

        private void EnqueueOcr(OcrRegionType type, Mat image, string kingdom, RunSettings settings)
        {
            const int maxQueuedPerRegion = 2;

            lock (_ocrQueueLock)
            {
                if (_ocrQueue.Count(item => item.Type == type) >= maxQueuedPerRegion)
                {
                    var kept = new Queue<OcrWorkItem>(_ocrQueue.Count);
                    var dropped = false;

                    while (_ocrQueue.Count > 0)
                    {
                        var item = _ocrQueue.Dequeue();
                        if (!dropped && item.Type == type)
                        {
                            item.Image.Dispose();
                            dropped = true;
                            continue;
                        }

                        kept.Enqueue(item);
                    }

                    while (kept.Count > 0)
                        _ocrQueue.Enqueue(kept.Dequeue());
                }

                _ocrQueue.Enqueue(new OcrWorkItem(type, image, kingdom, settings.Clone()));
            }
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

        private void ClearOcrQueue()
        {
            lock (_ocrQueueLock)
            {
                while (_ocrQueue.Count > 0)
                    _ocrQueue.Dequeue().Image.Dispose();
            }
        }

        private void ResetRegionStateIfKingdomChanged()
        {
            var currentKingdom = _state.CurrentKingdom;
            if (string.Equals(currentKingdom, _observedKingdom, StringComparison.OrdinalIgnoreCase))
                return;

            _history.Clear();
            _activeRegions.Clear();
            _lastHashes.Clear();
            _suppressStoryMoonUntilFrame = 0;
            ClearOcrQueue();
            _observedKingdom = currentKingdom;
        }

        // ----------------------------
        // RESULT HANDLING
        // ----------------------------
        private void Handle(OcrRegionType type, Moon match)
        {
            switch (type)
            {
                case OcrRegionType.Talkatoo:
                    _state.AddPending(match);
                    Console.WriteLine($"ADD: {match.English}");
                    break;

                case OcrRegionType.MoonGet:
                case OcrRegionType.StoryMoon:
                    var outcome = _state.MarkCollected(match);
                    switch (outcome)
                    {
                        case CollectionOutcome.Counted:
                            Console.WriteLine($"COLLECTED: {match.English} ({_state.CountedMoonCount} counted)");
                            break;

                        case CollectionOutcome.Uncounted:
                            Console.WriteLine($"UNCOUNTED: {match.English} ({_state.ActualMoonCount} actual, {_state.CountedMoonCount} counted)");
                            break;

                        case CollectionOutcome.AlreadyCounted:
                            Console.WriteLine($"SKIP COLLECTED: {match.English}");
                            break;

                        case CollectionOutcome.AlreadyUncounted:
                            Console.WriteLine($"SKIP UNCOUNTED: {match.English}");
                            break;

                        default:
                            Console.WriteLine($"IGNORED: {match.English}");
                            break;
                    }
                    break;
            }
        }

        private readonly record struct OcrWorkItem(OcrRegionType Type, Mat Image, string Kingdom, RunSettings Settings);
    }
}
