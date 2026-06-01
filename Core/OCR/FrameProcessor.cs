using OpenCvSharp;
using Aviscribe.Core.Capture;
using System;
using System.Collections.Concurrent;
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

        private readonly ConcurrentQueue<(OcrRegionType Type, Mat Image)> _ocrQueue = new();
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

            _regions =
            [
                //new(OcrRegionType.Talkatoo, new Rect(666, 828, 649, 113), _textDetector), // multiline
                new(OcrRegionType.Talkatoo, new Rect(666, 862, 649, 48), _textDetector, StableFrameCount: 1, StableImageMaxHammingDistance: 64), // single line
                new(
                    OcrRegionType.MoonGet,
                    new Rect(490, 797, 930, 60),
                    _textDetector,
                    StableFrameCount: 3,
                    StableImageMaxHammingDistance: 64,
                    DetectionBounds: new Rect(320, 600, 1250, 250),
                    DetectionIntervalFrames: 3),
                new(OcrRegionType.StoryMoon, new Rect(450, 820, 1100, 150), _textDetector, StableFrameCount: 12, DetectionIntervalFrames: 4)
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

                if (stable)
                {
                    if (!_activeRegions.Contains(region.Type))
                    {
                        using var ocrCrop = mat[region.Bounds];
                        ulong hash = ImageHash.Compute(ocrCrop);

                        if (!_lastHashes.TryGetValue(region.Type, out var lastHash) ||
                            ImageHash.Hamming(hash, lastHash) > 5)
                        {
                            _ocrQueue.Enqueue((region.Type, ocrCrop.Clone()));
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

        // ----------------------------
        // OCR LOOP (SLOW)
        // ----------------------------
        private void OcrLoop()
        {
            while (!_cts!.IsCancellationRequested)
            {
                if (!_ocrQueue.TryDequeue(out var item))
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

                    var result = item.Type == OcrRegionType.Talkatoo
                        ? _matcher.MatchTalkatooText(text, _state.CurrentKingdom, _state.Settings)
                        : _matcher.MatchCollectionText(text, _state.CurrentKingdom, _state.Settings);

                    if (result.IsAmbiguous)
                    {
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
    }
}
