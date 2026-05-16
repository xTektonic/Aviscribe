using OpenCvSharp;
using Aviscribe.Core.Capture;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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

        private readonly ConcurrentQueue<(OcrRegionType Type, Mat Image)> _ocrQueue = new();

        private readonly OcrRegion[] _regions =
        {
            //new(OcrRegionType.Talkatoo, new Rect(666, 828, 649, 113), TextDetection.HasTalkatooText), // multiline
            new(OcrRegionType.Talkatoo, new Rect(666, 862, 649, 48), TextDetection.HasTalkatooText), // single line
            new(OcrRegionType.MoonGet, new Rect(490, 797, 930, 60), TextDetection.HasMoonText)
        };

        public FrameProcessor(IOcrService ocr, MoonMatcher matcher, GameState state)
        {
            _ocr = ocr;
            _matcher = matcher;
            _state = state;
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
            Task.WaitAll(new[] { _frameWorker, _ocrWorker }, 1500);
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

            foreach (var region in _regions)
            {
                if (region.Type == OcrRegionType.MoonGet) continue;

                using var cropped = mat[region.Bounds];

                bool detected = region.Detection(cropped);
                //Console.WriteLine($"DETECTION ({region.Type}): {detected}");

                if (!_history.TryGetValue(region.Type, out var history))
                {
                    history = new RegionHistory(10);
                    _history[region.Type] = history;
                }

                history.Add(detected, cropped);

                bool stable = history.IsStableDetection() && history.IsStableImage();

                //Console.WriteLine($"STABILITY ({region.Type}): {stable}");

                if (stable)
                {
                    if (!_activeRegions.Contains(region.Type))
                    {
                        ulong hash = ImageHash.Compute(cropped);

                        if (!_lastHashes.TryGetValue(region.Type, out var lastHash) ||
                            ImageHash.Hamming(hash, lastHash) > 5)
                        {
                            _ocrQueue.Enqueue((region.Type, cropped.Clone()));
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

                    var result = _matcher.Match(text, _state.CurrentKingdom);

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
                    Console.WriteLine($"REMOVE: {match.English}");
                    break;
            }
        }
    }
}