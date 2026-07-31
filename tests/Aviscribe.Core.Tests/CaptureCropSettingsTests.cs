using Aviscribe.Core.Capture;
using OpenCvSharp;

namespace Aviscribe.Core.Tests;

public sealed class CaptureCropSettingsTests
{
    [Theory]
    [InlineData(1920, 1080, 0, 0, 1920, 1080)]
    [InlineData(1280, 720, 0, 0, 1280, 720)]
    [InlineData(1920, 1440, 0, 180, 1920, 1080)]
    [InlineData(2560, 1080, 320, 0, 1920, 1080)]
    [InlineData(640, 480, 0, 60, 640, 360)]
    public void InvalidOrDefaultCropFallsBackToLargestCentered16By9(
        int sourceWidth,
        int sourceHeight,
        int expectedX,
        int expectedY,
        int expectedWidth,
        int expectedHeight)
    {
        var legacy = new CaptureCropSettings
        {
            SourceWidth = 0,
            SourceHeight = 0,
            Width = 0,
            Height = 0
        };

        var actual = legacy.Resolve(sourceWidth, sourceHeight);

        Assert.Equal(
            new Rect(
                expectedX,
                expectedY,
                expectedWidth,
                expectedHeight),
            actual);
    }

    [Fact]
    public void SavedCropScalesWithChangedSourceResolution()
    {
        var saved = new CaptureCropSettings
        {
            SourceWidth = 1920,
            SourceHeight = 1080,
            X = 160,
            Y = 90,
            Width = 1600,
            Height = 900
        };

        Assert.Equal(
            new Rect(80, 45, 800, 450),
            saved.Resolve(960, 540));
    }

    [Fact]
    public void FromRectAlwaysProducesBounded16By9Selection()
    {
        var crop = CaptureCropSettings.FromRect(
            1280,
            1024,
            new Rect(-50, 900, 1500, 500));

        Assert.Equal(260, crop.X);
        Assert.Equal(529, crop.Y);
        Assert.Equal(880, crop.Width);
        Assert.Equal(495, crop.Height);
        Assert.Equal(16d / 9d, crop.Width / (double)crop.Height, 6);
    }
}
