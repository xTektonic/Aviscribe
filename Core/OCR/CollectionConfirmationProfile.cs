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
            RequiredAbsentObservations: 3,
            RetryPresentObservations: 2);

        internal static CollectionConfirmationProfile StoryMoon { get; } = new(
            OcrRegionType.StoryMoon,
            new Rect(450, 820, 1100, 150),
            new Rect(450, 820, 1100, 150),
            DetectionIntervalFrames: 1,
            RequiredPresentObservations: 2,
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
