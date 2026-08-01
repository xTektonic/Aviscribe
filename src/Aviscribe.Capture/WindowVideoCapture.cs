using Aviscribe.Core.Capture;
using OpenCvSharp;

namespace Aviscribe.Capture;

internal sealed class WindowVideoCapture : IVideoCapture
{
    private readonly IWindowCaptureBackend _backend;
    private readonly WindowCaptureTarget _target;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CancellationTokenSource? _runCancellation;
    private Task? _captureTask;
    private int _state = (int)CaptureState.Stopped;
    private int _disposed;
    private long _sequenceNumber;

    public WindowVideoCapture(IWindowCaptureBackend backend, WindowCaptureTarget target)
    {
        _backend = backend;
        _target = target;
        Device = new VideoDevice
        {
            Id = target.Id,
            Name = target.Name,
            Backend = backend.Name,
            Kind = CaptureSourceKind.Window,
            Capabilities = [new VideoFormat(target.Width, target.Height, "BGR", 10, 1, "Window")]
        };
        SelectedFormat = Device.Capabilities[0];
    }

    public event Action<VideoFrame>? FrameReceived;
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
            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            SetState(CaptureState.Running);
            _captureTask = Task.Run(() => CaptureLoopAsync(_runCancellation.Token), CancellationToken.None);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        Task? captureTask;
        try
        {
            if (State is CaptureState.Stopped or CaptureState.Disposed)
                return;

            SetState(CaptureState.Stopping);
            _runCancellation?.Cancel();
            captureTask = _captureTask;
            _captureTask = null;
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (captureTask != null)
        {
            try
            {
                await captureTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        _runCancellation?.Dispose();
        _runCancellation = null;
        SetState(CaptureState.Stopped);
    }

    private async Task CaptureLoopAsync(CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var captured = _backend.Capture(_target);
                if (captured.Empty())
                {
                    captured.Dispose();
                    throw new InvalidOperationException("The selected window did not produce a frame.");
                }

                consecutiveFailures = 0;
                SelectedFormat = new VideoFormat(captured.Width, captured.Height, "BGR", 10, 1, "Window");
                DispatchFrame(new VideoFrame(captured, DateTime.UtcNow, Interlocked.Increment(ref _sequenceNumber)));
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                consecutiveFailures++;
                if (consecutiveFailures >= 10)
                {
                    SetState(CaptureState.Faulted);
                    RaiseError(
                        $"Could not capture {_target.Name}: {ex.Message}",
                        ex,
                        deviceDisconnected: true);
                    return;
                }
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false);
        }
    }

    private void DispatchFrame(VideoFrame frame)
    {
        var handlers = FrameReceived?.GetInvocationList().Cast<Action<VideoFrame>>().ToArray();
        if (handlers == null || handlers.Length == 0)
        {
            frame.Dispose();
            return;
        }

        for (var index = 0; index < handlers.Length; index++)
        {
            var delivered = index == handlers.Length - 1 ? frame : frame.Clone();
            try
            {
                handlers[index](delivered);
            }
            catch (Exception ex)
            {
                delivered.Dispose();
                RaiseError($"A frame consumer failed: {ex.Message}", ex);
            }
        }
    }

    private void SetState(CaptureState state)
    {
        var previous = (CaptureState)Interlocked.Exchange(ref _state, (int)state);
        if (previous == state)
            return;
        try { StateChanged?.Invoke(this, new CaptureStateChangedEventArgs(previous, state)); } catch { }
    }

    private void RaiseError(string message, Exception? exception = null, bool deviceDisconnected = false)
    {
        try { CaptureFailed?.Invoke(this, new CaptureErrorEventArgs(message, exception, deviceDisconnected)); } catch { }
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        await StopAsync().ConfigureAwait(false);
        SetState(CaptureState.Disposed);
        _lifecycleGate.Dispose();
    }
}
