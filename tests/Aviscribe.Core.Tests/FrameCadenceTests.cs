using Aviscribe.Core.Ocr;

namespace Aviscribe.Core.Tests;

public sealed class FrameCadenceTests
{
    [Fact]
    public void TalkatooInspectionCadenceUsesElapsedTime()
    {
        var tracker = new TalkatooConfirmationTracker();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var inspected = Enumerable.Range(0, 30)
            .Where(frame => tracker.ShouldInspect(
                start + TimeSpan.FromSeconds(frame / 60.0)))
            .ToArray();

        Assert.Equal([0, 6, 12, 18, 24], inspected);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(30)]
    [InlineData(10)]
    public void TalkatooConfirmationUsesElapsedTimeAtAnyInputRate(int framesPerSecond)
    {
        using var prompt = new OpenCvSharp.Mat(
            new OpenCvSharp.Size(64, 48),
            OpenCvSharp.MatType.CV_8UC3,
            OpenCvSharp.Scalar.Black);
        OpenCvSharp.Cv2.Rectangle(
            prompt,
            new OpenCvSharp.Rect(2, 2, 50, 40),
            OpenCvSharp.Scalar.White,
            thickness: 2);
        var signature = TalkatooPromptSignature.Capture(prompt);
        var tracker = new TalkatooConfirmationTracker();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        TalkatooConfirmationDecision decision = default;
        var enqueuedFrame = -1;

        for (var frame = 0; frame < framesPerSecond; frame++)
        {
            var timestamp = start + TimeSpan.FromSeconds(
                frame / (double)framesPerSecond);
            decision = tracker.Observe(true, signature, timestamp);
            if (decision.ShouldEnqueue)
            {
                enqueuedFrame = frame;
                break;
            }
        }

        Assert.True(decision.ShouldEnqueue);
        Assert.Equal(framesPerSecond == 60 ? 2 : 1, enqueuedFrame);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(30)]
    [InlineData(10)]
    public void CollectionConfirmationUsesElapsedTimeAtAnyInputRate(
        int framesPerSecond)
    {
        var tracker = new CollectionConfirmationTracker(
            CollectionConfirmationProfile.StoryMoon);
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        CollectionConfirmationSnapshot snapshot = default;

        for (var frame = 0; frame < framesPerSecond; frame++)
        {
            snapshot = tracker.Observe(
                present: true,
                start + TimeSpan.FromSeconds(
                    frame / (double)framesPerSecond));
            if (snapshot.Confirmed)
                break;
        }

        Assert.True(snapshot.Confirmed);
        Assert.InRange(snapshot.ConsecutivePresent, 2, 3);
    }

    [Fact]
    public void AnimatedTalkatooPromptHasBoundedConfirmationAtTenFps()
    {
        using var first = CreateSignatureImage(left: 2);
        using var animated = CreateSignatureImage(left: 10);
        var tracker = new TalkatooConfirmationTracker();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        var initial = tracker.Observe(
            true,
            TalkatooPromptSignature.Capture(first),
            start);
        var confirmed = tracker.Observe(
            true,
            TalkatooPromptSignature.Capture(animated),
            start + TimeSpan.FromMilliseconds(100));

        Assert.False(initial.ShouldEnqueue);
        Assert.True(confirmed.ShouldEnqueue);
    }

    private static OpenCvSharp.Mat CreateSignatureImage(int left)
    {
        var image = new OpenCvSharp.Mat(
            new OpenCvSharp.Size(64, 48),
            OpenCvSharp.MatType.CV_8UC3,
            OpenCvSharp.Scalar.Black);
        OpenCvSharp.Cv2.Rectangle(
            image,
            new OpenCvSharp.Rect(left, 2, 30, 40),
            OpenCvSharp.Scalar.White,
            thickness: -1);
        return image;
    }
}
