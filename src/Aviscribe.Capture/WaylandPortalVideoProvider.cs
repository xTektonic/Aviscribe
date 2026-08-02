using Aviscribe.Core.Capture;
using OpenCvSharp;
using PipeWire.NET;
using System.Collections;
using System.Runtime.Versioning;
using Tmds.DBus;
using PipeWirePixelFormat = PipeWire.NET.PixelFormat;

namespace Aviscribe.Capture;

[SupportedOSPlatform("linux")]
internal sealed class WaylandPortalVideoProvider : IVideoProvider
{
    internal const string DeviceId = "linux:wayland-portal:choose-window";

    private static readonly VideoDevice PortalDevice = new()
    {
        Id = DeviceId,
        Name = "Choose a Wayland window…",
        Backend = "Wayland portal / PipeWire",
        Kind = CaptureSourceKind.Window,
        RequiresInteractiveSelection = true,
        Capabilities =
        [
            new VideoFormat(1920, 1080, "BGR", 30, 1, "Portal-selected window")
        ]
    };

    public IReadOnlyList<VideoDevice> GetDevices() => [PortalDevice];

    public IVideoCapture GetVideoCapture(string deviceId, string? formatId = null) =>
        throw new InvalidOperationException(
            "Wayland window selection is interactive and must be opened asynchronously.");

    public async ValueTask<IVideoCapture> OpenCaptureAsync(
        string deviceId,
        string? formatId = null,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(deviceId, DeviceId, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected Wayland source is not available.");

        var portal = await WaylandScreenCastPortal
            .ChooseWindowAsync(cancellationToken)
            .ConfigureAwait(false);
        return new WaylandPortalVideoCapture(PortalDevice, portal);
    }
}

[SupportedOSPlatform("linux")]
internal sealed class WaylandScreenCastPortal : IAsyncDisposable
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private static readonly ObjectPath DesktopPath =
        new("/org/freedesktop/portal/desktop");

    private readonly Connection _connection;
    private readonly ISession _session;

    private WaylandScreenCastPortal(
        Connection connection,
        ObjectPath sessionPath,
        uint nodeId)
    {
        _connection = connection;
        _session = connection.CreateProxy<ISession>(Service, sessionPath);
        NodeId = nodeId;
    }

    public uint NodeId { get; }

    public static async Task<WaylandScreenCastPortal> ChooseWindowAsync(
        CancellationToken cancellationToken)
    {
        var connection = new Connection(Address.Session);
        try
        {
            var connectionInfo = await connection.ConnectAsync().ConfigureAwait(false);
            var screenCast = connection.CreateProxy<IScreenCast>(Service, DesktopPath);
            var requestRoot = "/org/freedesktop/portal/desktop/request/" +
                connectionInfo.LocalName.TrimStart(':').Replace('.', '_') + "/";

            var createToken = NewToken("create");
            var createResponse = await InvokeRequestAsync(
                connection,
                requestRoot,
                createToken,
                () => screenCast.CreateSessionAsync(new Dictionary<string, object>
                {
                    ["handle_token"] = createToken,
                    ["session_handle_token"] = NewToken("session")
                }),
                cancellationToken).ConfigureAwait(false);
            EnsureAccepted(createResponse, "create a screen-cast session");
            if (!createResponse.Results.TryGetValue("session_handle", out var sessionValue) ||
                sessionValue is not ObjectPath sessionPath)
                throw new InvalidOperationException("The Wayland portal did not return a session.");

            var selectToken = NewToken("select");
            var selectResponse = await InvokeRequestAsync(
                connection,
                requestRoot,
                selectToken,
                () => screenCast.SelectSourcesAsync(
                    sessionPath,
                    new Dictionary<string, object>
                    {
                        ["handle_token"] = selectToken,
                        ["types"] = 2u,
                        ["multiple"] = false,
                        ["cursor_mode"] = 2u,
                        ["persist_mode"] = 1u
                    }),
                cancellationToken).ConfigureAwait(false);
            EnsureAccepted(selectResponse, "configure window capture");

            var startToken = NewToken("start");
            var startResponse = await InvokeRequestAsync(
                connection,
                requestRoot,
                startToken,
                () => screenCast.StartAsync(
                    sessionPath,
                    string.Empty,
                    new Dictionary<string, object>
                    {
                        ["handle_token"] = startToken
                    }),
                cancellationToken).ConfigureAwait(false);
            EnsureAccepted(startResponse, "select a window");
            if (!startResponse.Results.TryGetValue("streams", out var streams) ||
                !TryGetFirstNodeId(streams, out var nodeId))
                throw new InvalidOperationException("The Wayland portal did not return a PipeWire stream.");

            return new WaylandScreenCastPortal(connection, sessionPath, nodeId);
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static async Task<PortalResponse> InvokeRequestAsync(
        Connection connection,
        string requestRoot,
        string token,
        Func<Task<ObjectPath>> invoke,
        CancellationToken cancellationToken)
    {
        var expectedPath = new ObjectPath(requestRoot + token);
        var request = connection.CreateProxy<IRequest>(Service, expectedPath);
        var completion = new TaskCompletionSource<PortalResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = await request.WatchResponseAsync(response =>
            completion.TrySetResult(new PortalResponse(response.Response, response.Results)))
            .ConfigureAwait(false);
        using var cancellation = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));

        var returnedPath = await invoke().ConfigureAwait(false);
        if (returnedPath != expectedPath)
            throw new InvalidOperationException("The Wayland portal returned an unexpected request handle.");
        return await completion.Task.ConfigureAwait(false);
    }

    private static void EnsureAccepted(PortalResponse response, string action)
    {
        if (response.Response == 0)
            return;
        if (response.Response == 1)
            throw new OperationCanceledException($"The request to {action} was cancelled.");
        throw new InvalidOperationException($"The Wayland portal could not {action}.");
    }

    private static bool TryGetFirstNodeId(object streams, out uint nodeId)
    {
        nodeId = 0;
        if (streams is not IEnumerable items)
            return false;
        foreach (var item in items)
        {
            if (item == null)
                continue;
            var item1 = item.GetType().GetField("Item1")?.GetValue(item) ??
                item.GetType().GetProperty("Item1")?.GetValue(item);
            if (item1 == null)
                continue;
            nodeId = Convert.ToUInt32(item1, System.Globalization.CultureInfo.InvariantCulture);
            return nodeId != 0;
        }
        return false;
    }

    private static string NewToken(string prefix) =>
        $"aviscribe_{prefix}_{Guid.NewGuid():N}";

    public async ValueTask DisposeAsync()
    {
        try { await _session.CloseAsync().ConfigureAwait(false); }
        catch { }
        _connection.Dispose();
    }

    private sealed record PortalResponse(
        uint Response,
        IDictionary<string, object> Results);

    [DBusInterface("org.freedesktop.portal.ScreenCast")]
    private interface IScreenCast : IDBusObject
    {
        Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
        Task<ObjectPath> SelectSourcesAsync(
            ObjectPath sessionHandle,
            IDictionary<string, object> options);
        Task<ObjectPath> StartAsync(
            ObjectPath sessionHandle,
            string parentWindow,
            IDictionary<string, object> options);
    }

    [DBusInterface("org.freedesktop.portal.Request")]
    private interface IRequest : IDBusObject
    {
        Task<IDisposable> WatchResponseAsync(
            Action<(uint Response, IDictionary<string, object> Results)> handler);
    }

    [DBusInterface("org.freedesktop.portal.Session")]
    private interface ISession : IDBusObject
    {
        Task CloseAsync();
    }
}

[SupportedOSPlatform("linux")]
internal sealed class WaylandPortalVideoCapture : IVideoCapture
{
    private readonly WaylandScreenCastPortal _portal;
    private PipeWireContext? _context;
    private PipeWireVideoCapture? _capture;
    private int _state = (int)CaptureState.Stopped;
    private int _disposed;
    private long _sequence;

    public WaylandPortalVideoCapture(
        VideoDevice device,
        WaylandScreenCastPortal portal)
    {
        Device = new VideoDevice
        {
            Id = device.Id,
            Name = "Selected Wayland window",
            Backend = device.Backend,
            Kind = device.Kind,
            Capabilities = device.Capabilities
        };
        _portal = portal;
        SelectedFormat = device.Capabilities[0];
    }

    public event Action<Core.Capture.VideoFrame>? FrameReceived;
    public event EventHandler<CaptureStateChangedEventArgs>? StateChanged;
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public VideoDevice Device { get; }
    public VideoFormat SelectedFormat { get; private set; }
    public CaptureState State => (CaptureState)Volatile.Read(ref _state);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
        if (State == CaptureState.Running)
            return;

        SetState(CaptureState.Starting);
        try
        {
            _context = new PipeWireContext("Aviscribe");
            await _context.StartAsync(cancellationToken).ConfigureAwait(false);
            _capture = new PipeWireVideoCapture(_context, "Aviscribe Wayland window");
            _capture.FrameReady += OnFrameReady;
            _capture.Connect(
                _portal.NodeId,
                [
                    PipeWirePixelFormat.Bgra,
                    PipeWirePixelFormat.Rgba,
                    PipeWirePixelFormat.Bgrx,
                    PipeWirePixelFormat.Rgbx
                ]);
            SetState(CaptureState.Running);
        }
        catch (Exception ex)
        {
            SetState(CaptureState.Faulted);
            RaiseError($"Could not start Wayland window capture: {ex.Message}", ex);
            throw;
        }
    }

    private unsafe void OnFrameReady(object? sender, PipeWire.NET.VideoFrame frame)
    {
        if (frame.Data.IsEmpty || frame.Width <= 0 || frame.Height <= 0)
            return;
        try
        {
            var rowBytes = checked(frame.Width * 4);
            var packed = GC.AllocateUninitializedArray<byte>(
                checked(rowBytes * frame.Height));
            for (var row = 0; row < frame.Height; row++)
            {
                frame.Data.Slice(row * frame.Stride, rowBytes)
                    .CopyTo(packed.AsSpan(row * rowBytes, rowBytes));
            }

            fixed (byte* pointer = packed)
            {
                using var source = Mat.FromPixelData(
                    frame.Height,
                    frame.Width,
                    MatType.CV_8UC4,
                    (nint)pointer,
                    rowBytes);
                var bgr = new Mat();
                var conversion = frame.Format is
                    PipeWirePixelFormat.Rgba or PipeWirePixelFormat.Rgbx
                    ? ColorConversionCodes.RGBA2BGR
                    : ColorConversionCodes.BGRA2BGR;
                Cv2.CvtColor(source, bgr, conversion);
                SelectedFormat = new VideoFormat(
                    frame.Width,
                    frame.Height,
                    "BGR",
                    30,
                    1,
                    "Wayland portal window");
                Dispatch(new Core.Capture.VideoFrame(
                    bgr,
                    DateTime.UtcNow,
                    Interlocked.Increment(ref _sequence)));
            }
        }
        catch (Exception ex)
        {
            RaiseError($"Could not read a Wayland window frame: {ex.Message}", ex);
        }
    }

    private void Dispatch(Core.Capture.VideoFrame frame)
    {
        var handlers = FrameReceived?.GetInvocationList()
            .Cast<Action<Core.Capture.VideoFrame>>()
            .ToArray();
        if (handlers == null || handlers.Length == 0)
        {
            frame.Dispose();
            return;
        }
        for (var index = 0; index < handlers.Length; index++)
        {
            var delivered = index == handlers.Length - 1 ? frame : frame.Clone();
            try { handlers[index](delivered); }
            catch { delivered.Dispose(); }
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (State is CaptureState.Stopped or CaptureState.Disposed)
            return;
        SetState(CaptureState.Stopping);
        if (_capture != null)
        {
            _capture.FrameReady -= OnFrameReady;
            await _capture.DisposeAsync().ConfigureAwait(false);
            _capture = null;
        }
        if (_context != null)
        {
            await _context.DisposeAsync().ConfigureAwait(false);
            _context = null;
        }
        SetState(CaptureState.Stopped);
    }

    private void SetState(CaptureState state)
    {
        var previous = (CaptureState)Interlocked.Exchange(ref _state, (int)state);
        if (previous != state)
        {
            try { StateChanged?.Invoke(this, new CaptureStateChangedEventArgs(previous, state)); }
            catch { }
        }
    }

    private void RaiseError(string message, Exception exception)
    {
        try { CaptureFailed?.Invoke(this, new CaptureErrorEventArgs(message, exception)); }
        catch { }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await StopAsync().ConfigureAwait(false);
        await _portal.DisposeAsync().ConfigureAwait(false);
        SetState(CaptureState.Disposed);
    }
}
