using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using OpenCvSharp;
using PipeWire.NET;
using System.Runtime.Versioning;
using Tmds.DBus;
using PipeWirePixelFormat = PipeWire.NET.PixelFormat;

namespace Aviscribe.Capture;

[SupportedOSPlatform("linux")]
internal sealed class WaylandPortalVideoProvider : IVideoProvider
{
    internal const string DeviceId = "linux:wayland-portal:choose-window";
    internal const uint WindowSourceType = 2;

    private readonly object _sync = new();
    private readonly IAppDiagnostics _diagnostics;
    private readonly Func<CancellationToken, Task<WaylandPortalCapabilities>> _probe;
    private VideoDevice _device = CreateUnavailableDevice(
        "Checking whether the desktop window picker is available.");

    public WaylandPortalVideoProvider(IAppDiagnostics? diagnostics = null)
        : this(
            cancellationToken => WaylandScreenCastPortal.ProbeAsync(cancellationToken),
            diagnostics)
    {
    }

    internal WaylandPortalVideoProvider(
        Func<CancellationToken, Task<WaylandPortalCapabilities>> probe,
        IAppDiagnostics? diagnostics = null)
    {
        _probe = probe;
        _diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
    }

    public IReadOnlyList<VideoDevice> GetDevices()
    {
        lock (_sync)
            return [_device];
    }

    public async ValueTask<IReadOnlyList<VideoDevice>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        VideoDevice device;
        try
        {
            var capabilities = await _probe(cancellationToken).ConfigureAwait(false);
            _diagnostics.Information(
                "Wayland portal capabilities: " +
                $"version {capabilities.Version}, " +
                $"source types 0x{capabilities.AvailableSourceTypes:x}, " +
                $"cursor modes 0x{capabilities.AvailableCursorModes:x}.");

            device = (capabilities.AvailableSourceTypes & WindowSourceType) != 0
                ? CreateAvailableDevice()
                : CreateUnavailableDevice(
                    "This desktop portal does not support choosing individual windows. " +
                    "XWayland windows can still be selected when listed.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _diagnostics.Error("Wayland portal capability discovery failed.", ex);
            device = CreateUnavailableDevice(
                "The desktop window picker is unavailable. Ensure PipeWire, " +
                "xdg-desktop-portal, and a portal backend for your desktop are running.");
        }

        lock (_sync)
        {
            _device = device;
            return [_device];
        }
    }

    public IVideoCapture GetVideoCapture(string deviceId, string? formatId = null) =>
        throw new InvalidOperationException(
            "Wayland window selection is interactive and must be opened asynchronously.");

    public ValueTask<IVideoCapture> OpenCaptureAsync(
        string deviceId,
        string? formatId = null,
        CancellationToken cancellationToken = default) =>
        OpenCaptureAsync(
            deviceId,
            formatId,
            CaptureOpenOptions.Default,
            cancellationToken);

    public async ValueTask<IVideoCapture> OpenCaptureAsync(
        string deviceId,
        string? formatId,
        CaptureOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (!string.Equals(deviceId, DeviceId, StringComparison.Ordinal))
            throw new InvalidOperationException("The selected Wayland source is not available.");

        VideoDevice device;
        lock (_sync)
            device = _device;
        if (!device.IsAvailable)
            throw new InvalidOperationException(device.UnavailableReason);

        var portal = await WaylandScreenCastPortal
            .ChooseWindowAsync(
                options.ParentWindowIdentifier,
                _diagnostics,
                cancellationToken)
            .ConfigureAwait(false);
        return new WaylandPortalVideoCapture(device, portal, _diagnostics);
    }

    private static VideoDevice CreateAvailableDevice() => new()
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

    private static VideoDevice CreateUnavailableDevice(string reason) => new()
    {
        Id = DeviceId,
        Name = "Wayland window capture unavailable",
        Backend = "Wayland portal / PipeWire",
        Kind = CaptureSourceKind.Window,
        RequiresInteractiveSelection = true,
        IsAvailable = false,
        UnavailableReason = reason,
        Capabilities = []
    };
}

internal readonly record struct WaylandPortalCapabilities(
    uint Version,
    uint AvailableSourceTypes,
    uint AvailableCursorModes);

[SupportedOSPlatform("linux")]
internal sealed class WaylandScreenCastPortal : IAsyncDisposable
{
    private const string Service = "org.freedesktop.portal.Desktop";
    private static readonly ObjectPath DesktopPath =
        new("/org/freedesktop/portal/desktop");

    private readonly Connection _connection;
    private readonly ISession _session;
    private readonly CloseSafeHandle _pipeWireRemote;
    private IDisposable? _closedWatcher;
    private int _disposed;

    private WaylandScreenCastPortal(
        Connection connection,
        ObjectPath sessionPath,
        uint nodeId,
        CloseSafeHandle pipeWireRemote)
    {
        _connection = connection;
        _session = connection.CreateProxy<ISession>(Service, sessionPath);
        _pipeWireRemote = pipeWireRemote;
        NodeId = nodeId;
    }

    public event Action? Closed;

    public uint NodeId { get; }
    public System.Runtime.InteropServices.SafeHandle PipeWireRemote => _pipeWireRemote;

    public static async Task<WaylandPortalCapabilities> ProbeAsync(
        CancellationToken cancellationToken)
    {
        using var connection = new Connection(Address.Session);
        await connection.ConnectAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
        var screenCast = connection.CreateProxy<IScreenCast>(Service, DesktopPath);
        var versionTask = screenCast.GetAsync<uint>("version");
        var sourceTypesTask = screenCast.GetAsync<uint>("AvailableSourceTypes");
        var cursorModesTask = screenCast.GetAsync<uint>("AvailableCursorModes");
        await Task.WhenAll(versionTask, sourceTypesTask, cursorModesTask)
            .WaitAsync(cancellationToken)
            .ConfigureAwait(false);
        return new WaylandPortalCapabilities(
            await versionTask.ConfigureAwait(false),
            await sourceTypesTask.ConfigureAwait(false),
            await cursorModesTask.ConfigureAwait(false));
    }

    public static async Task<WaylandScreenCastPortal> ChooseWindowAsync(
        string? parentWindow,
        IAppDiagnostics diagnostics,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        var connection = new Connection(Address.Session);
        ISession? createdSession = null;
        CloseSafeHandle? pipeWireRemote = null;
        WaylandScreenCastPortal? ownedPortal = null;
        try
        {
            diagnostics.Information("Wayland portal: connecting to the desktop portal.");
            var connectionInfo = await connection.ConnectAsync()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            var screenCast = connection.CreateProxy<IScreenCast>(Service, DesktopPath);
            var requestRoot = "/org/freedesktop/portal/desktop/request/" +
                connectionInfo.LocalName.TrimStart(':').Replace('.', '_') + "/";

            diagnostics.Information("Wayland portal: creating a screen-cast session.");
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
                !TryGetObjectPath(sessionValue, out var sessionPath))
            {
                throw new InvalidOperationException(
                    "The Wayland portal did not return a screen-cast session.");
            }

            createdSession = connection.CreateProxy<ISession>(Service, sessionPath);

            diagnostics.Information("Wayland portal: requesting one window source.");
            var selectToken = NewToken("select");
            var selectResponse = await InvokeRequestAsync(
                connection,
                requestRoot,
                selectToken,
                () => screenCast.SelectSourcesAsync(
                    sessionPath,
                    CreateSelectSourcesOptions(selectToken)),
                cancellationToken).ConfigureAwait(false);
            EnsureAccepted(selectResponse, "configure window capture");

            diagnostics.Information("Wayland portal: opening the desktop window picker.");
            var startToken = NewToken("start");
            var startResponse = await InvokeRequestAsync(
                connection,
                requestRoot,
                startToken,
                () => screenCast.StartAsync(
                    sessionPath,
                    parentWindow ?? string.Empty,
                    new Dictionary<string, object>
                    {
                        ["handle_token"] = startToken
                    }),
                cancellationToken).ConfigureAwait(false);
            EnsureAccepted(startResponse, "select a window");
            if (!startResponse.Results.TryGetValue("streams", out var streams) ||
                !TryGetFirstNodeId(streams, out var nodeId))
            {
                throw new InvalidOperationException(
                    "The Wayland portal accepted the selection but returned no PipeWire stream.");
            }

            diagnostics.Information("Wayland portal: opening the authorized PipeWire remote.");
            pipeWireRemote = await screenCast.OpenPipeWireRemoteAsync(
                    sessionPath,
                    new Dictionary<string, object>())
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (pipeWireRemote.IsInvalid || pipeWireRemote.IsClosed)
            {
                throw new InvalidOperationException(
                    "The Wayland portal returned an invalid PipeWire remote descriptor.");
            }

            ownedPortal = new WaylandScreenCastPortal(
                connection,
                sessionPath,
                nodeId,
                pipeWireRemote);
            pipeWireRemote = null;
            createdSession = null;
            await ownedPortal.WatchSessionClosedAsync().ConfigureAwait(false);
            diagnostics.Information(
                $"Wayland portal: session is ready for PipeWire node {nodeId}.");
            var result = ownedPortal;
            ownedPortal = null;
            return result;
        }
        catch (OperationCanceledException)
        {
            diagnostics.Information("Wayland portal: window selection was cancelled.");
            if (ownedPortal != null)
                await ownedPortal.DisposeAsync().ConfigureAwait(false);
            else if (createdSession != null)
                await TryCloseSessionAsync(createdSession).ConfigureAwait(false);
            pipeWireRemote?.Dispose();
            if (ownedPortal == null)
                connection.Dispose();
            throw;
        }
        catch (Exception ex)
        {
            diagnostics.Error("Wayland portal setup failed.", ex);
            if (ownedPortal != null)
                await ownedPortal.DisposeAsync().ConfigureAwait(false);
            else if (createdSession != null)
                await TryCloseSessionAsync(createdSession).ConfigureAwait(false);
            pipeWireRemote?.Dispose();
            if (ownedPortal == null)
                connection.Dispose();
            throw new InvalidOperationException(
                $"Could not open the Wayland desktop window picker: {ex.Message}",
                ex);
        }
    }

    private async Task WatchSessionClosedAsync()
    {
        _closedWatcher = await _session.WatchClosedAsync(() =>
        {
            if (Volatile.Read(ref _disposed) == 0)
                Closed?.Invoke();
        }).ConfigureAwait(false);
    }

    internal static async Task<PortalResponse> InvokeRequestAsync(
        Connection connection,
        string requestRoot,
        string token,
        Func<Task<ObjectPath>> invoke,
        CancellationToken cancellationToken)
    {
        var expectedPath = new ObjectPath(requestRoot + token);
        var request = connection.CreateProxy<IRequest>(Service, expectedPath);
        return await InvokeRequestAsync(
            request,
            expectedPath,
            invoke,
            cancellationToken).ConfigureAwait(false);
    }

    internal static async Task<PortalResponse> InvokeRequestAsync(
        IRequest request,
        ObjectPath expectedPath,
        Func<Task<ObjectPath>> invoke,
        CancellationToken cancellationToken)
    {
        var completion = new TaskCompletionSource<PortalResponse>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        using var watcher = await request.WatchResponseAsync(
            response => completion.TrySetResult(
                new PortalResponse(response.Response, response.Results)),
            exception => completion.TrySetException(exception))
            .ConfigureAwait(false);
        using var cancellation = cancellationToken.Register(() =>
            completion.TrySetCanceled(cancellationToken));

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var returnedPath = await invoke()
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            if (returnedPath != expectedPath)
            {
                throw new InvalidOperationException(
                    "The Wayland portal returned an unexpected request handle.");
            }

            return await completion.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { await request.CloseAsync().ConfigureAwait(false); }
            catch { }
            throw;
        }
    }

    internal static void EnsureAccepted(PortalResponse response, string action)
    {
        if (response.Response == 0)
            return;
        if (response.Response == 1)
            throw new OperationCanceledException(
                $"The request to {action} was cancelled.");
        throw new InvalidOperationException(
            $"The Wayland portal rejected the request to {action} " +
            $"(response code {response.Response}).");
    }

    internal static bool TryGetFirstNodeId(object? streams, out uint nodeId)
    {
        if (streams is ValueTuple<uint, IDictionary<string, object>>[] typedStreams &&
            typedStreams.Length > 0 &&
            typedStreams[0].Item1 != 0)
        {
            nodeId = typedStreams[0].Item1;
            return true;
        }

        nodeId = 0;
        return false;
    }

    internal static IDictionary<string, object> CreateSelectSourcesOptions(
        string handleToken) =>
        new Dictionary<string, object>
        {
            ["handle_token"] = handleToken,
            ["types"] = WaylandPortalVideoProvider.WindowSourceType,
            ["multiple"] = false
        };

    internal static bool TryGetObjectPath(object? value, out ObjectPath objectPath)
    {
        if (value is ObjectPath typedPath)
        {
            objectPath = typedPath;
            return true;
        }

        // The portal specification keeps session_handle as a string for
        // backward compatibility even though it contains an object path.
        if (value is string text && text.StartsWith("/", StringComparison.Ordinal))
        {
            try
            {
                objectPath = new ObjectPath(text);
                return true;
            }
            catch (ArgumentException)
            {
            }
        }

        objectPath = default;
        return false;
    }

    private static string NewToken(string prefix) =>
        $"aviscribe_{prefix}_{Guid.NewGuid():N}";

    private static async Task TryCloseSessionAsync(ISession session)
    {
        try { await session.CloseAsync().ConfigureAwait(false); }
        catch { }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _closedWatcher?.Dispose();
        _closedWatcher = null;
        await TryCloseSessionAsync(_session).ConfigureAwait(false);
        _pipeWireRemote.Dispose();
        _connection.Dispose();
    }

    internal sealed record PortalResponse(
        uint Response,
        IDictionary<string, object> Results);

    [DBusInterface("org.freedesktop.portal.ScreenCast")]
    internal interface IScreenCast : IDBusObject
    {
        Task<ObjectPath> CreateSessionAsync(IDictionary<string, object> options);
        Task<ObjectPath> SelectSourcesAsync(
            ObjectPath sessionHandle,
            IDictionary<string, object> options);
        Task<ObjectPath> StartAsync(
            ObjectPath sessionHandle,
            string parentWindow,
            IDictionary<string, object> options);
        Task<CloseSafeHandle> OpenPipeWireRemoteAsync(
            ObjectPath sessionHandle,
            IDictionary<string, object> options);
        Task<T> GetAsync<T>(string property);
    }

    [DBusInterface("org.freedesktop.portal.Request")]
    internal interface IRequest : IDBusObject
    {
        Task CloseAsync();
        Task<IDisposable> WatchResponseAsync(
            Action<(uint Response, IDictionary<string, object> Results)> handler,
            Action<Exception>? onError = null);
    }

    [DBusInterface("org.freedesktop.portal.Session")]
    internal interface ISession : IDBusObject
    {
        Task CloseAsync();
        Task<IDisposable> WatchClosedAsync(Action handler);
    }
}

[SupportedOSPlatform("linux")]
internal sealed class WaylandPortalVideoCapture : IVideoCapture
{
    private static readonly TimeSpan StreamTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(10);

    private readonly WaylandScreenCastPortal _portal;
    private readonly IAppDiagnostics _diagnostics;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private PipeWireContext? _context;
    private PipeWireVideoCapture? _capture;
    private TaskCompletionSource<bool>? _firstFrame;
    private int _state = (int)CaptureState.Stopped;
    private int _disposed;
    private int _emptyFrameReported;
    private long _sequence;

    public WaylandPortalVideoCapture(
        VideoDevice device,
        WaylandScreenCastPortal portal,
        IAppDiagnostics? diagnostics = null)
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
        _diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
        _portal.Closed += OnPortalClosed;
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
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == CaptureState.Running)
                return;

            SetState(CaptureState.Starting);
            var streaming = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _firstFrame = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            Volatile.Write(ref _emptyFrameReported, 0);

            try
            {
                _diagnostics.Information("Wayland PipeWire: connecting through the portal remote.");
                _context = new PipeWireContext("Aviscribe");
                await _context.StartRemoteAsync(_portal.PipeWireRemote, cancellationToken)
                    .ConfigureAwait(false);
                _capture = new PipeWireVideoCapture(
                    _context,
                    "Aviscribe Wayland window",
                    PipeWireBufferPolicy.HostMemory);
                _capture.FrameReady += OnFrameReady;
                _capture.StateChanged += (_, _, newState) =>
                {
                    if (newState == PipeWireStreamState.Streaming)
                        streaming.TrySetResult(true);
                    else if (newState == PipeWireStreamState.Error)
                        streaming.TrySetException(new InvalidOperationException(
                            "The PipeWire stream entered an error state."));
                };
                _capture.Connect(
                    _portal.NodeId,
                    [
                        PipeWirePixelFormat.Bgra,
                        PipeWirePixelFormat.Rgba,
                        PipeWirePixelFormat.Bgrx,
                        PipeWirePixelFormat.Rgbx
                    ]);

                await streaming.Task
                    .WaitAsync(StreamTimeout, cancellationToken)
                    .ConfigureAwait(false);
                await _firstFrame.Task
                    .WaitAsync(FirstFrameTimeout, cancellationToken)
                    .ConfigureAwait(false);
                SetState(CaptureState.Running);
                _diagnostics.Information("Wayland PipeWire: streaming CPU-readable frames.");
            }
            catch (TimeoutException ex)
            {
                await CleanupPipeWireAsync().ConfigureAwait(false);
                SetState(CaptureState.Faulted);
                var message = streaming.Task.IsCompletedSuccessfully
                    ? "PipeWire connected but no window frame arrived within 10 seconds."
                    : "PipeWire did not begin streaming the selected window within 10 seconds.";
                RaiseError(message, ex);
                throw new InvalidOperationException(message, ex);
            }
            catch (Exception ex)
            {
                await CleanupPipeWireAsync().ConfigureAwait(false);
                SetState(CaptureState.Faulted);
                RaiseError($"Could not start Wayland window capture: {ex.Message}", ex);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private unsafe void OnFrameReady(PipeWireVideoCapture sender, PipeWire.NET.VideoFrame frame)
    {
        if (frame.Data.IsEmpty || frame.Width <= 0 || frame.Height <= 0)
        {
            if (Interlocked.Exchange(ref _emptyFrameReported, 1) == 0)
            {
                var exception = new InvalidOperationException(
                    "PipeWire supplied a frame that is not readable from CPU memory.");
                _firstFrame?.TrySetException(exception);
                RaiseError(exception.Message, exception);
            }
            return;
        }

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
                _firstFrame?.TrySetResult(true);
                Dispatch(new Core.Capture.VideoFrame(
                    bgr,
                    DateTime.UtcNow,
                    Interlocked.Increment(ref _sequence)));
            }
        }
        catch (Exception ex)
        {
            _firstFrame?.TrySetException(ex);
            RaiseError($"Could not read a Wayland window frame: {ex.Message}", ex);
        }
    }

    private void OnPortalClosed()
    {
        if (Volatile.Read(ref _disposed) != 0)
            return;
        var exception = new InvalidOperationException(
            "The desktop portal closed the selected window capture session.");
        _firstFrame?.TrySetException(exception);
        SetState(CaptureState.Faulted);
        RaiseError(exception.Message, exception);
        _ = CleanupAfterPortalClosedAsync();
    }

    private async Task CleanupAfterPortalClosedAsync()
    {
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try { await CleanupPipeWireAsync().ConfigureAwait(false); }
        catch (Exception ex) { _diagnostics.Error("Could not clean up the closed Wayland session.", ex); }
        finally { _lifecycleGate.Release(); }
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
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is CaptureState.Stopped or CaptureState.Disposed)
                return;
            SetState(CaptureState.Stopping);
            await CleanupPipeWireAsync().ConfigureAwait(false);
            SetState(CaptureState.Stopped);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task CleanupPipeWireAsync()
    {
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
        _firstFrame = null;
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
        _diagnostics.Error(message, exception);
        try { CaptureFailed?.Invoke(this, new CaptureErrorEventArgs(message, exception)); }
        catch { }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _portal.Closed -= OnPortalClosed;
        await _lifecycleGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await CleanupPipeWireAsync().ConfigureAwait(false);
            await _portal.DisposeAsync().ConfigureAwait(false);
            SetState(CaptureState.Disposed);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }
}
