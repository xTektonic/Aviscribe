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
    public void AcceptsTypedAndStringPortalObjectPaths()
    {
        const string expected = "/org/freedesktop/portal/desktop/session/1_42/aviscribe";

        Assert.True(WaylandScreenCastPortal.TryGetObjectPath(
            new ObjectPath(expected), out var typedPath));
        Assert.Equal(expected, typedPath.ToString());

        Assert.True(WaylandScreenCastPortal.TryGetObjectPath(
            expected, out var stringPath));
        Assert.Equal(expected, stringPath.ToString());

        Assert.False(WaylandScreenCastPortal.TryGetObjectPath(
            "not/an/object/path", out _));
    }

    [Fact]
    public async Task AdvertisesInteractiveWindowSourceWhenPortalSupportsWindows()
    {
        if (!OperatingSystem.IsLinux())
            return;

        IVideoProvider provider = new WaylandPortalVideoProvider(_ =>
            Task.FromResult(new WaylandPortalCapabilities(5, 3, 7)));

        var source = Assert.Single(await provider.RefreshAsync(
            TestContext.Current.CancellationToken));
        Assert.Equal(WaylandPortalVideoProvider.DeviceId, source.Id);
        Assert.Equal(CaptureSourceKind.Window, source.Kind);
        Assert.True(source.RequiresInteractiveSelection);
        Assert.True(source.IsAvailable);
        Assert.Contains("Choose", source.Name, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DisablesPortalSourceWhenWindowCapabilityIsMissing()
    {
        if (!OperatingSystem.IsLinux())
            return;

        IVideoProvider provider = new WaylandPortalVideoProvider(_ =>
            Task.FromResult(new WaylandPortalCapabilities(5, 1, 7)));

        var source = Assert.Single(await provider.RefreshAsync(
            TestContext.Current.CancellationToken));

        Assert.False(source.IsAvailable);
        Assert.Contains("individual windows", source.UnavailableReason, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await provider.OpenCaptureAsync(
                source.Id,
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task MissingPortalBecomesUnavailableSource()
    {
        if (!OperatingSystem.IsLinux())
            return;

        IVideoProvider provider = new WaylandPortalVideoProvider(_ =>
            Task.FromException<WaylandPortalCapabilities>(
                new InvalidOperationException("service missing")));

        var source = Assert.Single(await provider.RefreshAsync(
            TestContext.Current.CancellationToken));

        Assert.False(source.IsAvailable);
        Assert.Contains("xdg-desktop-portal", source.UnavailableReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildsMinimalSingleWindowSelectionOptions()
    {
        var options = WaylandScreenCastPortal.CreateSelectSourcesOptions("token");

        Assert.Equal("token", options["handle_token"]);
        Assert.Equal(2u, options["types"]);
        Assert.Equal(false, options["multiple"]);
        Assert.DoesNotContain("cursor_mode", options.Keys);
        Assert.DoesNotContain("persist_mode", options.Keys);
    }

    [Fact]
    public void DecodesTypedPortalStreamsWithoutReflection()
    {
        object streams = new ValueTuple<uint, IDictionary<string, object>>[]
        {
            new(42, new Dictionary<string, object>())
        };

        Assert.True(WaylandScreenCastPortal.TryGetFirstNodeId(streams, out var nodeId));
        Assert.Equal(42u, nodeId);
        Assert.False(WaylandScreenCastPortal.TryGetFirstNodeId(Array.Empty<object>(), out _));
    }

    [Fact]
    public void DistinguishesPortalCancellationAndRejection()
    {
        var cancelled = new WaylandScreenCastPortal.PortalResponse(
            1,
            new Dictionary<string, object>());
        var rejected = new WaylandScreenCastPortal.PortalResponse(
            2,
            new Dictionary<string, object>());

        Assert.Throws<OperationCanceledException>(() =>
            WaylandScreenCastPortal.EnsureAccepted(cancelled, "select a window"));
        var error = Assert.Throws<InvalidOperationException>(() =>
            WaylandScreenCastPortal.EnsureAccepted(rejected, "select a window"));
        Assert.Contains("response code 2", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SubscribesBeforeRequestAndClosesItOnCancellation()
    {
        var path = new ObjectPath(
            "/org/freedesktop/portal/desktop/request/1_42/test");
        var request = new StubRequest(path);
        using var cancellation = new CancellationTokenSource();
        var responseTask = WaylandScreenCastPortal.InvokeRequestAsync(
            request,
            path,
            () =>
            {
                Assert.True(request.IsWatching);
                return Task.FromResult(path);
            },
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => responseTask);
        Assert.Equal(1, request.CloseCount);
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

    private sealed class StubRequest(ObjectPath objectPath) : WaylandScreenCastPortal.IRequest
    {
        public ObjectPath ObjectPath => objectPath;
        public bool IsWatching { get; private set; }
        public int CloseCount { get; private set; }

        public Task CloseAsync()
        {
            CloseCount++;
            return Task.CompletedTask;
        }

        public Task<IDisposable> WatchResponseAsync(
            Action<(uint Response, IDictionary<string, object> Results)> handler,
            Action<Exception>? onError = null)
        {
            IsWatching = true;
            return Task.FromResult<IDisposable>(new StubDisposable());
        }
    }

    private sealed class StubDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
