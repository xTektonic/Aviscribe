using Aviscribe.Core.Capture;
using OpenCvSharp;

namespace Aviscribe.Capture;

internal sealed class WindowVideoCapture : IVideoCapture
{
    internal const int TargetFramesPerSecond =
        CaptureTiming.PreferredFramesPerSecond;
    private static readonly TimeSpan FrameInterval =
        CaptureTiming.PreferredFrameInterval;

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
            Capabilities = [CreateFormat(target.Width, target.Height)]
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

            if (_captureTask != null)
                await _captureTask.ConfigureAwait(false);
            SetState(CaptureState.Starting);
            _runCancellation?.Dispose();
            _runCancellation = new CancellationTokenSource();
            var token = _runCancellation.Token;
            using var startupCancellation = cancellationToken.Register(_runCancellation.Cancel);
            var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _captureTask = Task.Run(() => CaptureLoopAsync(started, token), CancellationToken.None);
            try
            {
                // Report success only after the native backend produces a frame.
                await started.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                _runCancellation.Cancel();
                await _captureTask.ConfigureAwait(false);
                _captureTask = null;
                startupCancellation.Dispose();
                _runCancellation.Dispose();
                _runCancellation = null;
                if (cancellationToken.IsCancellationRequested)
                    SetState(CaptureState.Stopped);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
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
            _runCancellation?.Cancel();
            var captureTask = _captureTask;
            _captureTask = null;
            if (captureTask != null)
            {
                await captureTask.ConfigureAwait(false);
            }
            _runCancellation?.Dispose();
            _runCancellation = null;
            SetState(CaptureState.Stopped);
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private async Task CaptureLoopAsync(TaskCompletionSource started, CancellationToken cancellationToken)
    {
        try
        {
            using var session = _backend.OpenSession(_target);
            await ReadFramesAsync(session, started, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            started.TrySetCanceled(cancellationToken);
        }
        catch (Exception ex)
        {
            SetState(CaptureState.Faulted);
            RaiseError($"Could not capture {_target.Name}: {ex.Message}", ex);
            started.TrySetException(ex);
        }
    }

    private async Task ReadFramesAsync(
        IWindowCaptureSession session, TaskCompletionSource started, CancellationToken cancellationToken)
    {
        var consecutiveFailures = 0;
        using var frameTimer = new PeriodicTimer(FrameInterval);
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var captured = session.Capture();
                if (cancellationToken.IsCancellationRequested)
                {
                    captured.Dispose();
                    cancellationToken.ThrowIfCancellationRequested();
                }
                if (captured.Empty())
                {
                    captured.Dispose();
                    throw new InvalidOperationException("The selected window did not produce a frame.");
                }

                consecutiveFailures = 0;
                SelectedFormat = CreateFormat(captured.Width, captured.Height);
                if (!started.Task.IsCompleted)
                {
                    SetState(CaptureState.Running);
                    started.TrySetResult();
                }
                DispatchFrame(new VideoFrame(captured, DateTime.UtcNow, Interlocked.Increment(ref _sequenceNumber)));
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                consecutiveFailures++;
                if (!started.Task.IsCompleted || consecutiveFailures >= 10)
                    throw;
            }

            if (!await frameTimer
                .WaitForNextTickAsync(cancellationToken)
                .ConfigureAwait(false))
            {
                break;
            }
        }
    }

    internal static VideoFormat CreateFormat(int width, int height) =>
        new(width, height, "BGR", TargetFramesPerSecond, 1, "Window");

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
