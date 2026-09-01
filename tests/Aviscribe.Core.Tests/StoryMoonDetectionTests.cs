using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Core.Tests;

public sealed class StoryMoonDetectionTests
{
    private static readonly Rect[] BackgroundPatches =
    [
        new(1030, 56, 24, 24),
        new(1074, 4, 24, 24),
        new(946, 8, 24, 24),
        new(250, 124, 24, 24)
    ];

    private static readonly Scalar[] BackgroundColors =
    [
        new(2.1, 0.7, 225.1),
        new(2.7, 0.9, 224.9),
        new(9.4, 4.1, 224.6),
        new(10.7, 5.0, 225.3)
    ];

    [Fact]
    public void MatchingBackgroundIsDetectedWithoutMoonNamePixels()
    {
        using var image = CreateMatchingBackground();

        Assert.True(TextDetection.HasStoryMoonText(image));
    }

    [Fact]
    public void OneObscuredBackgroundPatchIsTolerated()
    {
        using var image = CreateMatchingBackground();
        Fill(image, BackgroundPatches[0], Scalar.Black);

        Assert.True(TextDetection.HasStoryMoonText(image));
    }

    [Fact]
    public void TwoObscuredBackgroundPatchesAreRejected()
    {
        using var image = CreateMatchingBackground();
        Fill(image, BackgroundPatches[0], Scalar.Black);
        Fill(image, BackgroundPatches[1], Scalar.Black);

        Assert.False(TextDetection.HasStoryMoonText(image));
    }

    [Fact]
    public void NarrowMoonNameDoesNotControlDetection()
    {
        using var image = CreateMatchingBackground();
        Cv2.Rectangle(
            image,
            new Rect(520, 60, 12, 32),
            Scalar.White,
            thickness: -1);

        Assert.True(TextDetection.HasStoryMoonText(image));
    }

    [Fact]
    public void WrongRedShadeIsRejected()
    {
        using var image = new Mat(
            new Size(1100, 150),
            MatType.CV_8UC3,
            new Scalar(70, 65, 165));

        Assert.False(TextDetection.HasStoryMoonText(image));
    }

    [Fact]
    public void TexturedRedBackgroundIsRejected()
    {
        using var image = CreateMatchingBackground();
        foreach (var bounds in BackgroundPatches)
        {
            for (var y = bounds.Top; y < bounds.Bottom; y++)
            {
                for (var x = bounds.Left; x < bounds.Right; x++)
                {
                    image.Set(
                        y,
                        x,
                        (x + y) % 2 == 0
                            ? new Vec3b(0, 0, 225)
                            : new Vec3b(0, 0, 120));
                }
            }
        }

        Assert.False(TextDetection.HasStoryMoonText(image));
    }

    [Fact]
    public void InvalidImagesAreRejected()
    {
        using var empty = new Mat();
        using var grayscale = new Mat(
            new Size(1100, 150),
            MatType.CV_8UC1,
            Scalar.Black);
        using var undersized = new Mat(
            new Size(1090, 140),
            MatType.CV_8UC3,
            Scalar.Black);

        Assert.False(TextDetection.HasStoryMoonText(empty));
        Assert.False(TextDetection.HasStoryMoonText(grayscale));
        Assert.False(TextDetection.HasStoryMoonText(undersized));
    }

    [Fact]
    public void TwoConsecutiveDetectionsConfirmAndReleaseNormally()
    {
        using var image = CreateMatchingBackground();
        var tracker = new CollectionConfirmationTracker(
            CollectionConfirmationProfile.StoryMoon);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var interval = CollectionConfirmationProfile.StoryMoon.DetectionInterval;

        var first = tracker.Observe(
            TextDetection.HasStoryMoonText(image),
            start);
        var second = tracker.Observe(
            TextDetection.HasStoryMoonText(image),
            start + interval);

        Assert.False(first.Confirmed);
        Assert.True(second.Confirmed);
        Assert.True(tracker.RecordEnqueued(second.Generation, attempt: 1));
        tracker.RecordOutcome(second.Generation, resolved: true);

        for (var index = 1;
            index < CollectionConfirmationProfile.StoryMoon.RequiredAbsentObservations;
            index++)
        {
            Assert.True(tracker.Observe(
                present: false,
                start + TimeSpan.FromSeconds(1) +
                    interval * (index - 1)).Active);
        }

        var released = tracker.Observe(
            present: false,
            start + TimeSpan.FromSeconds(1) +
                interval *
                    (CollectionConfirmationProfile.StoryMoon
                        .RequiredAbsentObservations - 1));
        Assert.False(
            released.Active,
            $"absence={released.ConsecutiveAbsentDuration}, " +
            $"required={CollectionConfirmationProfile.StoryMoon.RequiredAbsentDuration}");
    }

    [Fact]
    public void FailedStoryMoonReadRetriesOnAMeaningfullyLaterFrame()
    {
        var profile = CollectionConfirmationProfile.StoryMoon;
        var tracker = new CollectionConfirmationTracker(profile);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        tracker.Observe(present: true, start);
        var confirmed = tracker.Observe(
            present: true,
            start + profile.DetectionInterval);
        Assert.True(tracker.RecordEnqueued(confirmed.Generation, attempt: 1));
        tracker.RecordOutcome(confirmed.Generation, resolved: false);

        CollectionConfirmationSnapshot retry = default;
        for (var index = 0;
            index < profile.RetryPresentObservations;
            index++)
        {
            retry = tracker.Observe(
                present: true,
                start + profile.DetectionInterval * (index + 2));
            if (index < profile.RetryPresentObservations - 1)
                Assert.False(retry.CanEnqueueRetry(profile));
        }

        Assert.True(retry.CanEnqueueRetry(profile));
        Assert.True(
            retry.ConsecutivePresentDuration >= TimeSpan.FromMilliseconds(250));
    }

    private static Mat CreateMatchingBackground()
    {
        var image = new Mat(
            new Size(1100, 150),
            MatType.CV_8UC3,
            Scalar.Black);

        for (var index = 0; index < BackgroundPatches.Length; index++)
            Fill(image, BackgroundPatches[index], BackgroundColors[index]);

        return image;
    }

    private static void Fill(Mat image, Rect bounds, Scalar color)
    {
        Cv2.Rectangle(image, bounds, color, thickness: -1);
    }
}
