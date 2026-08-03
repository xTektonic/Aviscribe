using Aviscribe.Core.KingdomDetection;
using OpenCvSharp;

namespace Aviscribe.Core.Tests;

public sealed class TemplateKingdomDetectorTests
{
    public static TheoryData<string, string> Templates => new()
    {
        { "Cascade", "cascade.png" },
        { "Sand", "sand.png" },
        { "Wooded", "wooded.png" },
        { "Lake", "lake.png" },
        { "Lost", "lost.png" },
        { "Metro", "metro.png" },
        { "Seaside", "seaside.png" },
        { "Snow", "snow.png" },
        { "Luncheon", "luncheon.png" },
        { "Bowsers", "bowsers.png" },
        { "Moon", "moon.png" },
        { "Mushroom", "mushroom.png" },
        { "Cap", "cap.png" }
    };

    [Theory]
    [MemberData(nameof(Templates))]
    public void CanonicalHudSymbolsMapToTheirKingdom(
        string expectedKingdom,
        string fileName)
    {
        using var detector = CreateDetector();
        using var frame = CreateHudFrame(fileName);

        var result = detector.Detect(frame);

        Assert.Equal(KingdomDetectionStatus.Matched, result.Status);
        Assert.Equal(expectedKingdom, result.Kingdom);
        Assert.True(result.Score >= TemplateKingdomDetector.DefaultMinimumScore);
    }

    [Fact]
    public void MissingHudReturnsNoResult()
    {
        using var detector = CreateDetector();
        using var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);

        var result = detector.Detect(frame);

        Assert.Equal(KingdomDetectionStatus.HudNotVisible, result.Status);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void UnderlineWithoutAnIconReturnsNoResult()
    {
        using var detector = CreateDetector();
        using var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        DrawHudUnderline(frame);

        var result = detector.Detect(frame);

        Assert.Equal(KingdomDetectionStatus.LowConfidence, result.Status);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void TintedCollectionStateNeverMapsToAnotherKingdom()
    {
        using var detector = CreateDetector();
        using var frame = CreateHudFrame("cascade.png");
        using (var icon = new Mat(
                   frame,
                   TemplateKingdomDetector.IconTemplateBounds))
        using (var purple = new Mat(
                   icon.Rows,
                   icon.Cols,
                   icon.Type(),
                   new Scalar(190, 45, 190)))
        {
            Cv2.AddWeighted(icon, 0.65, purple, 0.35, 0, icon);
        }

        var result = detector.Detect(frame);

        Assert.True(
            !result.IsMatch || result.Kingdom == "Cascade",
            $"Unexpected match: {result.Kingdom} ({result.Score:0.000})");
    }

    private static TemplateKingdomDetector CreateDetector()
    {
        return new TemplateKingdomDetector(AppPaths.KingdomIconTemplateFolder);
    }

    private static Mat CreateHudFrame(string fileName)
    {
        var frame = new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black);
        using var icon = Cv2.ImRead(
            Path.Combine(AppPaths.KingdomIconTemplateFolder, fileName),
            ImreadModes.Color);
        using (var destination = new Mat(
                   frame,
                   TemplateKingdomDetector.IconTemplateBounds))
        {
            icon.CopyTo(destination);
        }

        DrawHudUnderline(frame);
        return frame;
    }

    private static void DrawHudUnderline(Mat frame)
    {
        Cv2.Rectangle(
            frame,
            new Rect(68, 130, 340, 9),
            Scalar.White,
            thickness: -1);
    }
}
