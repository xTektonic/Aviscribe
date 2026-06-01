using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public enum OcrRegionType
    {
        Talkatoo,
        MoonGet,
        StoryMoon
    }

    public record OcrRegion(
        OcrRegionType Type,
        Rect Bounds,
        ITextPresenceDetector Detector,
        int StableFrameCount = 10,
        int StableImageMaxHammingDistance = 12,
        Rect? DetectionBounds = null,
        int DetectionIntervalFrames = 1
    );
}
