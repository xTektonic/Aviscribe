using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Core.Tests;

public sealed class GameplayFrameNormalizerTests
{
    [Fact]
    public void CropAndNormalizationArePixelAccurate()
    {
        using var source = CreateTwoColorSource();
        var crop = new CaptureCropSettings
        {
            SourceWidth = 32,
            SourceHeight = 18,
            X = 16,
            Y = 0,
            Width = 16,
            Height = 9
        };

        using var normalized =
            GameplayFrameNormalizer.Normalize(source, crop);

        Assert.Equal(OcrReferenceLayout.Width, normalized.Width);
        Assert.Equal(OcrReferenceLayout.Height, normalized.Height);
        Assert.Equal(
            new Vec3b(0, 255, 0),
            normalized.At<Vec3b>(
                OcrReferenceLayout.Height / 2,
                OcrReferenceLayout.Width / 2));
    }

    [Fact]
    public void ApplyingNewCropChangesTheNextNormalizedFrame()
    {
        using var source = CreateTwoColorSource();
        var left = new CaptureCropSettings
        {
            SourceWidth = 32,
            SourceHeight = 18,
            X = 0,
            Y = 0,
            Width = 16,
            Height = 9
        };
        var right = new CaptureCropSettings
        {
            SourceWidth = 32,
            SourceHeight = 18,
            X = 16,
            Y = 0,
            Width = 16,
            Height = 9
        };

        using var before = GameplayFrameNormalizer.Normalize(source, left);
        using var after = GameplayFrameNormalizer.Normalize(source, right);

        Assert.Equal(
            new Vec3b(255, 0, 0),
            before.At<Vec3b>(540, 960));
        Assert.Equal(
            new Vec3b(0, 255, 0),
            after.At<Vec3b>(540, 960));
    }

    [Fact]
    public void ReferenceFrameCanUseTheSingleNoResizePath()
    {
        using var source = new Mat(
            OcrReferenceLayout.Height,
            OcrReferenceLayout.Width,
            MatType.CV_8UC3);

        Assert.True(GameplayFrameNormalizer.IsAlreadyNormalized(
            source,
            CaptureCropSettings.Default));
    }

    private static Mat CreateTwoColorSource()
    {
        var source = new Mat(18, 32, MatType.CV_8UC3);
        source.SetTo(new Scalar(255, 0, 0));
        using var right = new Mat(source, new Rect(16, 0, 16, 18));
        right.SetTo(new Scalar(0, 255, 0));
        return source;
    }
}
