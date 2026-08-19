using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using OpenCvSharp;

namespace Aviscribe.Capture;

public sealed class WindowCaptureProvider : IVideoProvider
{
    private readonly object _sync = new();
    private readonly IWindowCaptureBackend _backend;
    private IReadOnlyList<VideoDevice> _sources = [];
    private Dictionary<string, WindowCaptureTarget> _targets = new(StringComparer.Ordinal);

    public WindowCaptureProvider()
        : this(WindowCaptureBackendFactory.Create())
    {
    }

    internal WindowCaptureProvider(IWindowCaptureBackend backend)
    {
        _backend = backend;
    }

    public IReadOnlyList<VideoDevice> GetDevices()
    {
        lock (_sync)
        {
            RefreshLocked();
            return _sources;
        }
    }

    public IVideoCapture GetVideoCapture(string deviceId, string? formatId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        lock (_sync)
        {
            if (!_targets.TryGetValue(deviceId, out var target))
            {
                RefreshLocked();
                if (!_targets.TryGetValue(deviceId, out target))
                    throw new InvalidOperationException("The selected window is no longer available. Refresh the window list and select it again.");
            }

            if (!target.IsAvailable)
            {
                if (_backend.TryRequestAccess())
                {
                    RefreshLocked();
                    throw new InvalidOperationException(
                        "Window-capture permission was granted. Refresh the window list and choose a window.");
                }
                throw new InvalidOperationException(target.UnavailableReason);
            }

            return new WindowVideoCapture(_backend, target);
        }
    }

    public ValueTask<IReadOnlyList<VideoDevice>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetDevices());
    }

    private void RefreshLocked()
    {
        var targets = _backend.EnumerateTargets();
        _targets = targets.ToDictionary(target => target.Id, StringComparer.Ordinal);
        _sources = targets.Select(target => new VideoDevice
        {
            Id = target.Id,
            Name = target.Name,
            Backend = _backend.Name,
            Kind = CaptureSourceKind.Window,
            IsAvailable = target.IsAvailable,
            UnavailableReason = target.UnavailableReason,
            Capabilities = target.IsAvailable
                ? [WindowVideoCapture.CreateFormat(target.Width, target.Height)]
                : []
        }).ToArray();
    }
}

public static class PlatformWindowCaptureProvider
{
    public static IVideoProvider Create(IAppDiagnostics? diagnostics = null)
    {
        if (OperatingSystem.IsLinux() &&
            !string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            var portalProvider = new WaylandPortalVideoProvider(diagnostics);
            if (string.IsNullOrWhiteSpace(
                    Environment.GetEnvironmentVariable("DISPLAY")))
            {
                return portalProvider;
            }

            return new CompositeVideoProvider(
                portalProvider,
                new WindowCaptureProvider(new X11WindowCaptureBackend()));
        }

        return new WindowCaptureProvider();
    }
}

internal interface IWindowCaptureBackend
{
    string Name { get; }
    IReadOnlyList<WindowCaptureTarget> EnumerateTargets();
    Mat Capture(WindowCaptureTarget target);
    bool TryRequestAccess() => false;
}

internal sealed record WindowCaptureTarget(
    string Id,
    string Name,
    nint NativeHandle,
    int Width,
    int Height,
    bool IsAvailable = true,
    string UnavailableReason = "");

internal static class WindowCaptureBackendFactory
{
    public static IWindowCaptureBackend Create()
    {
        if (OperatingSystem.IsWindows())
            return new WindowsWindowCaptureBackend();
        if (OperatingSystem.IsMacOS())
            return new MacWindowCaptureBackend();
        if (OperatingSystem.IsLinux())
            return new X11WindowCaptureBackend();
        return new UnsupportedWindowCaptureBackend("Window capture is not supported on this operating system.");
    }
}

internal sealed class UnsupportedWindowCaptureBackend(string reason) : IWindowCaptureBackend
{
    public string Name => "Unavailable";

    public IReadOnlyList<WindowCaptureTarget> EnumerateTargets() =>
    [
        new WindowCaptureTarget(
            "window:unavailable",
            reason,
            0,
            0,
            0,
            false,
            reason)
    ];

    public Mat Capture(WindowCaptureTarget target) =>
        throw new PlatformNotSupportedException(reason);
}
