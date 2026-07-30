using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    internal sealed record CollectionConfirmationProfile(
        OcrRegionType RegionType,
        Rect OcrBounds,
        Rect DetectionBounds,
        int DetectionIntervalFrames,
        int RequiredPresentObservations,
        int RequiredAbsentObservations,
        int RetryPresentObservations)
    {
        internal static CollectionConfirmationProfile MoonGet { get; } = new(
            OcrRegionType.MoonGet,
            new Rect(490, 797, 930, 60),
            new Rect(320, 600, 1250, 250),
            DetectionIntervalFrames: 5,
            RequiredPresentObservations: 1,
            // Named videos contain gaps up to 24 sampled absences within one overlay;
            // the shortest verified gap between distinct events is 125.
            RequiredAbsentObservations: 30,
            RetryPresentObservations: 2);

        internal static CollectionConfirmationProfile StoryMoon { get; } = new(
            OcrRegionType.StoryMoon,
            new Rect(450, 820, 1100, 150),
            new Rect(450, 820, 1100, 150),
            DetectionIntervalFrames: 1,
            RequiredPresentObservations: 2,
            // Named videos contain gaps up to four observations within one overlay.
            RequiredAbsentObservations: 8,
            RetryPresentObservations: 3);

        internal static CollectionConfirmationProfile For(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.MoonGet => MoonGet,
                OcrRegionType.StoryMoon => StoryMoon,
                _ => throw new ArgumentOutOfRangeException(
                    nameof(regionType),
                    regionType,
                    "Only collection regions have collection confirmation profiles.")
            };
        }
    }
}
