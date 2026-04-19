using OpenCvSharp;
using Aviscribe.Core.Capture;

namespace Aviscribe.Core.Ocr
{
    public class FrameProcessor
    {
        private readonly IOcrService _ocr;
        private readonly MoonMatcher _matcher;
        private readonly GameState _state;

        private Rect _textRegion = new Rect(685, 851, 607, 52);

        public FrameProcessor(IOcrService ocr, MoonMatcher matcher, GameState state)
        {
            _ocr = ocr;
            _matcher = matcher;
            _state = state;
        }

        public void ProcessFrame(VideoFrame frame)
        {
            var mat = frame.Frame;

            if (mat.Empty())
                return;

            using var cropped = new Mat(mat, _textRegion);
            using var processed = Preprocess(cropped);

            var text = _ocr.ReadText(processed);

            if (string.IsNullOrWhiteSpace(text))
                return;

            var result = _matcher.Match(text, _state.CurrentKingdom);

            if (result.BestMatch != null)
                _state.AddPending(result.BestMatch);

            Console.WriteLine($"OCR: {text}");
        }

        private Mat Preprocess(Mat input)
        {
            using var gray = new Mat();
            Cv2.CvtColor(input, gray, ColorConversionCodes.BGR2GRAY);

            var thresh = new Mat();
            Cv2.Threshold(gray, thresh, 160, 255, ThresholdTypes.Binary);

            return thresh;
        }
    }
}