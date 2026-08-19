namespace Aviscribe.Capture.Tests;

public sealed class FlashCapVideoProviderTests
{
    [Fact]
    public void AutomaticFormatSelectionPrefersSixtyFramesPerSecond()
    {
        var sixty = FlashCapVideoProvider.FrameRatePreferencePenalty(60);
        var ntscSixty = FlashCapVideoProvider.FrameRatePreferencePenalty(59.94);
        var oneTwenty = FlashCapVideoProvider.FrameRatePreferencePenalty(120);
        var thirty = FlashCapVideoProvider.FrameRatePreferencePenalty(30);
        var ten = FlashCapVideoProvider.FrameRatePreferencePenalty(10);

        Assert.True(sixty < ntscSixty);
        Assert.True(ntscSixty < oneTwenty);
        Assert.True(oneTwenty < thirty);
        Assert.True(thirty < ten);
    }
}
