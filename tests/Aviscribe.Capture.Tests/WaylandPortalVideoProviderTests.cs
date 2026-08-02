using Aviscribe.Core.Capture;
using System.Runtime.Versioning;

namespace Aviscribe.Capture.Tests;

[SupportedOSPlatform("linux")]
public sealed class WaylandPortalVideoProviderTests
{
    [Fact]
    public void AdvertisesSingleInteractiveWindowSource()
    {
        if (!OperatingSystem.IsLinux())
            return;

        IVideoProvider provider = new WaylandPortalVideoProvider();

        var source = Assert.Single(provider.GetDevices());
        Assert.Equal(WaylandPortalVideoProvider.DeviceId, source.Id);
        Assert.Equal(CaptureSourceKind.Window, source.Kind);
        Assert.True(source.RequiresInteractiveSelection);
        Assert.True(source.IsAvailable);
        Assert.Contains("Choose", source.Name, StringComparison.Ordinal);
    }

    [Fact]
    public void SynchronousOpenExplainsInteractiveRequirement()
    {
        if (!OperatingSystem.IsLinux())
            return;

        IVideoProvider provider = new WaylandPortalVideoProvider();

        var error = Assert.Throws<InvalidOperationException>(() =>
            provider.GetVideoCapture(WaylandPortalVideoProvider.DeviceId));
        Assert.Contains("interactive", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}
