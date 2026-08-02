using Aviscribe.Core.Capture;
using System.Runtime.Versioning;
using Tmds.DBus;

namespace Aviscribe.Capture.Tests;

[SupportedOSPlatform("linux")]
public sealed class WaylandPortalVideoProviderTests
{
    [Fact]
    public void DbusProxyContractsAreAccessibleToGeneratedAssembly()
    {
        using var connection = new Connection(
            "unix:path=/tmp/aviscribe-dbus-proxy-test");

        var proxy = connection.CreateProxy<WaylandScreenCastPortal.IScreenCast>(
            "org.freedesktop.portal.Desktop",
            new ObjectPath("/org/freedesktop/portal/desktop"));

        Assert.NotNull(proxy);
    }

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
