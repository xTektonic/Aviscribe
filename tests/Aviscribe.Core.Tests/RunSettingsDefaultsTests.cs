using Aviscribe.Core.Ocr;

namespace Aviscribe.Core.Tests;

public sealed class RunSettingsDefaultsTests
{
    [Fact]
    public void UsesPreferredFirstRunDefaults()
    {
        var settings = new RunSettings();

        Assert.True(settings.AutomaticallySwitchKingdoms);
        Assert.True(settings.ShowPendingMoonImages);
        Assert.Equal(OcrMode.WebGpu, settings.OcrMode);
        Assert.False(settings.WoodedBeforeLake);
        Assert.False(settings.SeasideBeforeSnow);
    }
}
