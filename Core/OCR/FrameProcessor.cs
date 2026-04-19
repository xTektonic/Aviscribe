using OpenCvSharp;
using Aviscribe.Core.Capture;

namespace Aviscribe.Core.Ocr
{
    public class FrameProcessor
    {
        private readonly IOcrService _ocr;
        private readonly MoonMatcher _matcher;
        private readonly GameState _state;

        private VideoFrame? _latestFrame;
        private readonly object _lock = new();

        private CancellationTokenSource? _cts;
        private Task? _worker;

        private readonly Mat _gray = new();
        private readonly Mat _thresh = new();

        private Mat? _lastOcrRegion;

        private readonly Rect _textRegion = new Rect(685, 851, 607, 52);

        // 🟢 NEW: OCR throttling (key improvement)
        private DateTime _lastOcrTime = DateTime.MinValue;
        private readonly TimeSpan _ocrCooldown = TimeSpan.FromMilliseconds(500);

        public FrameProcessor(IOcrService ocr, MoonMatcher matcher, GameState state)
        {
            _ocr = ocr;
            _matcher = matcher;
            _state = state;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();

            _worker = Task.Run(() =>
            {
                var token = _cts.Token;

                while (!token.IsCancellationRequested)
                {
                    ProcessLatestFrame();
                    Thread.Sleep(166);
                }
            }, _cts.Token);
        }

        public void Stop()
        {
            _cts?.Cancel();
            _worker?.Wait(500);
        }

        public void PushFrame(VideoFrame frame)
        {
            lock (_lock)
            {
                _latestFrame?.Dispose();
                _latestFrame = frame;
            }
        }

        private void ProcessLatestFrame()
        {
            VideoFrame? frame;

            lock (_lock)
            {
                frame = _latestFrame;
                _latestFrame = null;
            }

            if (frame == null)
                return;

            try
            {
                ProcessFrame(frame);
            }
            finally
            {
                frame.Dispose();
            }
        }

        private void ProcessFrame(VideoFrame frame)
        {
            var mat = frame.Frame;

            if (mat.Empty())
                return;

            using Mat cropped = mat[_textRegion].Clone();

            // 🟢 FAST PATH: skip expensive comparison if cooldown active
            if (!ShouldRunOcr(cropped))
                return;

            Preprocess(cropped);

            var text = _ocr.ReadText(_thresh);

            if (string.IsNullOrWhiteSpace(text))
                return;

            var result = _matcher.Match(text, _state.CurrentKingdom);

            if (result.BestMatch != null)
                _state.AddPending(result.BestMatch);

            Console.WriteLine($"OCR: {text}");
        }

        // 🟢 NEW: unified gating logic
        private bool ShouldRunOcr(Mat current)
        {
            // 1. cooldown gate (biggest win)
            if (DateTime.UtcNow - _lastOcrTime < _ocrCooldown)
                return false;

            // 2. similarity gate
            if (_lastOcrRegion != null)
            {
                if (_lastOcrRegion.Size() == current.Size())
                {
                    double diff = Cv2.Norm(current, _lastOcrRegion, NormTypes.L2);

                    if (diff < 1.0)
                        return false;
                }

                _lastOcrRegion.Dispose();
            }

            _lastOcrRegion = current.Clone();
            _lastOcrTime = DateTime.UtcNow;

            return true;
        }

        private void Preprocess(Mat input)
        {
            Cv2.CvtColor(input, _gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(_gray, _thresh, 160, 255, ThresholdTypes.Binary);
        }
    }
}