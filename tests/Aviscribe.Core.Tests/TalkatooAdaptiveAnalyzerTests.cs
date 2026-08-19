using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Core.Tests;

public sealed class TalkatooAdaptiveAnalyzerTests
{
    [Fact]
    public void StrictPromptUsesUnmodifiedDetection()
    {
        using var prompt = CreatePrompt(dimmed: false);
        var analyzer = new TalkatooAdaptiveAnalyzer();

        var analysis = analyzer.Analyze(prompt);

        Assert.True(analysis.Present);
        Assert.False(analysis.Adapted);
        Assert.Equal(1.0, analysis.Gain);
        Assert.Null(analyzer.LockedGain);
    }

    [Fact]
    public void DimPromptUsesSmallestPassingGain()
    {
        using var prompt = CreatePrompt(dimmed: true);
        var analyzer = new TalkatooAdaptiveAnalyzer();

        Assert.False(TalkatooStaticGate.Analyze(prompt).Present);

        var analysis = analyzer.Analyze(prompt);

        Assert.True(analysis.Present);
        Assert.True(analysis.Adapted);
        Assert.True(analysis.StartedAdaptiveRun);
        Assert.Equal(1.10, analysis.Gain, precision: 2);
        Assert.Equal(1.10, analyzer.LockedGain!.Value, precision: 2);
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("text")]
    [InlineData("yellow-background")]
    public void IncompleteCandidatesRemainAbsentAtEveryGain(string kind)
    {
        using var image = new Mat(
            new Size(715, 48),
            MatType.CV_8UC3,
            Scalar.Black);

        switch (kind)
        {
            case "marker":
                DrawMarker(image, dimmed: true);
                break;
            case "text":
                DrawText(image, dimmed: true);
                break;
            case "yellow-background":
                image.SetTo(new Scalar(80, 190, 210));
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(kind));
        }

        var analysis = new TalkatooAdaptiveAnalyzer().Analyze(image);

        Assert.False(analysis.Present);
        Assert.False(analysis.Adapted);
    }

    [Fact]
    public void GainRemainsLockedUntilPromptIsAbsent()
    {
        using var dimPrompt = CreatePrompt(dimmed: true);
        using var strictPrompt = CreatePrompt(dimmed: false);
        using var absent = new Mat(
            new Size(715, 48),
            MatType.CV_8UC3,
            Scalar.Black);
        var analyzer = new TalkatooAdaptiveAnalyzer();

        var first = analyzer.Analyze(dimPrompt);
        var locked = analyzer.Analyze(strictPrompt);
        var missing = analyzer.Analyze(absent);
        var reacquired = analyzer.Analyze(strictPrompt);

        Assert.Equal(1.10, first.Gain, precision: 2);
        Assert.True(locked.Adapted);
        Assert.Equal(1.10, locked.Gain, precision: 2);
        Assert.False(locked.StartedAdaptiveRun);
        Assert.False(missing.Present);
        Assert.False(reacquired.Adapted);
        Assert.Equal(1.0, reacquired.Gain);
    }

    [Fact]
    public void AdaptiveSignatureIgnoresMovingYellowOutsideTextBand()
    {
        using var firstFrame = CreatePrompt(dimmed: true);
        using var secondFrame = firstFrame.Clone();
        var analyzer = new TalkatooAdaptiveAnalyzer();
        var analysis = analyzer.Analyze(firstFrame);
        Assert.True(analysis.Adapted);

        var backgroundBounds = new Rect(420, 42, 80, 5);
        Assert.False(analysis.Gate.TextBounds.IntersectsWith(backgroundBounds));
        Cv2.Rectangle(
            firstFrame,
            new Rect(420, 42, 35, 5),
            new Scalar(80, 190, 210),
            thickness: -1);
        Cv2.Rectangle(
            secondFrame,
            new Rect(465, 42, 35, 5),
            new Scalar(80, 190, 210),
            thickness: -1);

        var firstSignature = TalkatooPromptSignature.CaptureAdaptive(
            firstFrame,
            analysis);
        var secondSignature = TalkatooPromptSignature.CaptureAdaptive(
            secondFrame,
            analysis);

        Assert.Equal(analysis.Gate.MarkerBounds, firstSignature.MarkerBounds);
        Assert.True(firstSignature.IsNearIdenticalTo(secondSignature));

        var tracker = new TalkatooConfirmationTracker();
        var enqueueCount = 0;
        foreach (var signature in new[]
                 {
                     firstSignature,
                     secondSignature,
                     firstSignature,
                     secondSignature,
                     firstSignature,
                     secondSignature
                 })
        {
            var decision = tracker.Observe(present: true, signature);
            if (!decision.ShouldEnqueue)
                continue;

            enqueueCount++;
            Assert.True(tracker.RecordEnqueued(
                decision.Generation,
                decision.Attempt));
            tracker.RecordResolved(decision.Generation);
        }

        Assert.Equal(1, enqueueCount);
    }

    [Fact]
    public void AdaptiveSignatureToleratesSmallCaptureTranslation()
    {
        using var firstFrame = CreatePrompt(dimmed: true);
        using var shiftedFrame = CreatePrompt(
            dimmed: true,
            offsetX: 1,
            offsetY: 1);
        var firstAnalysis = new TalkatooAdaptiveAnalyzer().Analyze(firstFrame);
        var shiftedAnalysis = new TalkatooAdaptiveAnalyzer().Analyze(shiftedFrame);

        var firstSignature = TalkatooPromptSignature.CaptureAdaptive(
            firstFrame,
            firstAnalysis);
        var shiftedSignature = TalkatooPromptSignature.CaptureAdaptive(
            shiftedFrame,
            shiftedAnalysis);

        Assert.True(firstSignature.IsNearIdenticalTo(shiftedSignature));
    }

    [Fact]
    public void AdaptiveSignatureDistinguishesDifferentTextShapes()
    {
        using var firstFrame = CreatePrompt(dimmed: true);
        using var differentFrame = new Mat(
            new Size(715, 48),
            MatType.CV_8UC3,
            Scalar.Black);
        DrawMarker(differentFrame, dimmed: true);
        DrawAlternateText(differentFrame, dimmed: true);
        var firstAnalysis = new TalkatooAdaptiveAnalyzer().Analyze(firstFrame);
        var differentAnalysis = new TalkatooAdaptiveAnalyzer().Analyze(differentFrame);

        Assert.True(firstAnalysis.Present);
        Assert.True(differentAnalysis.Present);

        var firstSignature = TalkatooPromptSignature.CaptureAdaptive(
            firstFrame,
            firstAnalysis);
        var differentSignature = TalkatooPromptSignature.CaptureAdaptive(
            differentFrame,
            differentAnalysis);

        Assert.False(firstSignature.IsNearIdenticalTo(differentSignature));
    }

    [Fact]
    public void TwelveFramePromptEnqueuesAtWorstIdleCadenceAlignment()
    {
        using var absent = new Mat(
            new Size(715, 48),
            MatType.CV_8UC3,
            Scalar.Black);
        using var prompt = CreatePrompt(dimmed: true);
        var analyzer = new TalkatooAdaptiveAnalyzer();
        var tracker = new TalkatooConfirmationTracker();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(tracker.ShouldInspect(start));
        var absentAnalysis = analyzer.Analyze(absent);
        tracker.Observe(absentAnalysis.Present, signature: null, start);

        int? enqueuedPromptFrame = null;
        for (var promptFrame = 1; promptFrame <= 12; promptFrame++)
        {
            var timestamp = start + TimeSpan.FromSeconds(promptFrame / 60.0);
            if (!tracker.ShouldInspect(timestamp))
                continue;

            var analysis = analyzer.Analyze(prompt);
            Assert.True(analysis.Present);
            Assert.True(analysis.Adapted);
            var signature = TalkatooPromptSignature.CaptureAdaptive(
                prompt,
                analysis);
            var decision = tracker.Observe(
                present: true,
                signature,
                timestamp);
            if (!decision.ShouldEnqueue)
                continue;

            Assert.True(tracker.RecordEnqueued(
                decision.Generation,
                decision.Attempt));
            tracker.RecordResolved(decision.Generation);
            enqueuedPromptFrame = promptFrame;
            break;
        }

        Assert.Equal(8, enqueuedPromptFrame);
    }

    private static Mat CreatePrompt(
        bool dimmed,
        int offsetX = 0,
        int offsetY = 0)
    {
        var image = new Mat(
            new Size(715, 48),
            MatType.CV_8UC3,
            Scalar.Black);
        DrawMarker(image, dimmed, offsetX, offsetY);
        DrawText(image, dimmed, offsetX, offsetY);
        return image;
    }

    private static void DrawMarker(
        Mat image,
        bool dimmed,
        int offsetX = 0,
        int offsetY = 0)
    {
        var value = (byte)(dimmed ? 190 : 220);
        var color = new Vec3b(value, value, value);

        for (var y = 1 + offsetY; y < 43 + offsetY; y++)
        {
            for (var x = 20 + offsetX; x < 70 + offsetX; x++)
            {
                if (x < 25 + offsetX ||
                    x >= 65 + offsetX ||
                    y < 6 + offsetY ||
                    y >= 38 + offsetY)
                {
                    image.Set(y, x, color);
                }
            }
        }
    }

    private static void DrawText(
        Mat image,
        bool dimmed,
        int offsetX = 0,
        int offsetY = 0)
    {
        var color = dimmed
            ? new Vec3b(80, 190, 210)
            : new Vec3b(90, 215, 235);

        for (var left = 100 + offsetX; left < 280 + offsetX; left += 14)
        {
            for (var y = 8 + offsetY; y < 36 + offsetY; y++)
            {
                for (var x = left; x < left + 8; x++)
                    image.Set(y, x, color);
            }
        }
    }

    private static void DrawAlternateText(Mat image, bool dimmed)
    {
        var color = dimmed
            ? new Vec3b(80, 190, 210)
            : new Vec3b(90, 215, 235);

        for (var y = 8; y < 36; y++)
        {
            for (var x = 100; x < 280; x++)
            {
                if ((x / 7 + y / 7) % 2 == 0)
                    image.Set(y, x, color);
            }
        }
    }
}
