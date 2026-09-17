using OpenCvSharp;
using Aviscribe.Core.Capture;

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
        internal TimeSpan DetectionInterval =>
            CaptureTiming.DurationForFrames(DetectionIntervalFrames);

        internal TimeSpan RequiredPresentDuration =>
            CaptureTiming.DurationForFrames(
                Math.Max(0, RequiredPresentObservations - 1) *
                DetectionIntervalFrames);

        internal TimeSpan RequiredAbsentDuration =>
            CaptureTiming.DurationForFrames(
                Math.Max(0, RequiredAbsentObservations - 1) *
                DetectionIntervalFrames);

        internal TimeSpan RetryPresentDuration =>
            CaptureTiming.DurationForFrames(
                Math.Max(0, RetryPresentObservations - 1) *
                DetectionIntervalFrames);

        internal static CollectionConfirmationProfile MoonGet { get; } = new(
            OcrRegionType.MoonGet,
            OcrReferenceLayout.MoonGet.OcrBounds,
            OcrReferenceLayout.MoonGet.DetectionBounds,
            DetectionIntervalFrames: 5,
            RequiredPresentObservations: 1,
            // Named videos contain gaps up to 24 sampled absences within one overlay;
            // the shortest verified gap between distinct events is 125.
            RequiredAbsentObservations: 30,
            RetryPresentObservations: 2);

        internal static CollectionConfirmationProfile StoryMoon { get; } = new(
            OcrRegionType.StoryMoon,
            OcrReferenceLayout.StoryMoon.OcrBounds,
            OcrReferenceLayout.StoryMoon.DetectionBounds,
            DetectionIntervalFrames: 1,
            RequiredPresentObservations: 2,
            // Named videos contain gaps up to four observations within one overlay.
            RequiredAbsentObservations: 8,
            // A failed first read often occurs while the title is still animating.
            // Wait for a meaningfully later frame instead of immediately reading
            // the same transition/debug-overlay contamination a second time.
            RetryPresentObservations: 20);

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
