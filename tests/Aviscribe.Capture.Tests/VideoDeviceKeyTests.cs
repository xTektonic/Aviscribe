using Aviscribe.Core.Capture;

namespace Aviscribe.Capture.Tests;

public sealed class VideoDeviceKeyTests
{
    [Fact]
    public void SameNativeIdentityProducesStablePrivacyPreservingKey()
    {
        var first = VideoDeviceKey.Create(
            "Linux",
            "V4L2",
            "/dev/v4l/by-id/usb-capture-card",
            "Capture card");
        var second = VideoDeviceKey.Create(
            " linux ",
            " v4l2 ",
            "/dev/v4l/by-id/usb-capture-card",
            "Renamed card");

        Assert.Equal(first, second);
        Assert.StartsWith("linux:v4l2:", first);
        Assert.DoesNotContain("usb-capture-card", first);
    }

    [Fact]
    public void UnicodeEquivalentFallbackNamesProduceSameKey()
    {
        var composed = VideoDeviceKey.Create(
            "macos",
            "avfoundation",
            null,
            "Caméra");
        var decomposed = VideoDeviceKey.Create(
            "macos",
            "avfoundation",
            null,
            "Came\u0301ra");

        Assert.Equal(composed, decomposed);
    }

    [Fact]
    public void PlatformAndBackendRemainPartOfKey()
    {
        var windows = VideoDeviceKey.Create(
            "windows",
            "directshow",
            "device-1",
            "");
        var linux = VideoDeviceKey.Create(
            "linux",
            "v4l2",
            "device-1",
            "");

        Assert.NotEqual(windows, linux);
    }
}
