using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public sealed record OcrRegionGuide(
        OcrRegionType Type,
        Rect OcrBounds,
        Rect DetectionBounds);

    public static class OcrReferenceLayout
    {
        public const int Width = 1920;
        public const int Height = 1080;

        public static OcrRegionGuide Talkatoo { get; } = new(
            OcrRegionType.Talkatoo,
            new Rect(666, 862, 649, 48),
            new Rect(600, 862, 715, 48));

        public static OcrRegionGuide MoonGet { get; } = new(
            OcrRegionType.MoonGet,
            new Rect(490, 797, 930, 60),
            new Rect(320, 600, 1250, 250));

        public static OcrRegionGuide StoryMoon { get; } = new(
            OcrRegionType.StoryMoon,
            new Rect(450, 820, 1100, 150),
            new Rect(450, 820, 1100, 150));

        public static IReadOnlyList<OcrRegionGuide> Guides { get; } =
            [Talkatoo, MoonGet, StoryMoon];
    }
}
