using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal sealed class OcrAttemptCountingProxy : IOcrService
    {
        private readonly IOcrService _inner;
        private readonly object _lock = new();
        private int _talkatooAttempts;
        private int _moonGetAttempts;
        private int _storyMoonAttempts;

        public OcrAttemptCountingProxy(IOcrService inner)
        {
            _inner = inner;
        }

        public string ReadText(Mat frame)
        {
            var regionType = RegionType(frame);
            lock (_lock)
            {
                switch (regionType)
                {
                    case OcrRegionType.Talkatoo:
                        _talkatooAttempts++;
                        break;

                    case OcrRegionType.MoonGet:
                        _moonGetAttempts++;
                        break;

                    case OcrRegionType.StoryMoon:
                        _storyMoonAttempts++;
                        break;
                }
            }

            return _inner.ReadText(frame);
        }

        public OcrAttemptSnapshot Snapshot()
        {
            lock (_lock)
            {
                return new OcrAttemptSnapshot(
                    _talkatooAttempts,
                    _moonGetAttempts,
                    _storyMoonAttempts);
            }
        }

        private static OcrRegionType? RegionType(Mat frame)
        {
            return (frame.Width, frame.Height) switch
            {
                (649, 48) => OcrRegionType.Talkatoo,
                (930, 60) => OcrRegionType.MoonGet,
                (1100, 150) => OcrRegionType.StoryMoon,
                _ => null
            };
        }
    }

    internal readonly record struct OcrAttemptSnapshot(
        int TalkatooAttempts,
        int MoonGetAttempts,
        int StoryMoonAttempts)
    {
        public int TotalCollectionAttempts => MoonGetAttempts + StoryMoonAttempts;

        public static OcrAttemptSnapshot operator -(
            OcrAttemptSnapshot current,
            OcrAttemptSnapshot previous)
        {
            return new OcrAttemptSnapshot(
                current.TalkatooAttempts - previous.TalkatooAttempts,
                current.MoonGetAttempts - previous.MoonGetAttempts,
                current.StoryMoonAttempts - previous.StoryMoonAttempts);
        }
    }
}
