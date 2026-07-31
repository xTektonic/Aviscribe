using Aviscribe.Core.Capture;
using FlashCap;
using OpenCvSharp;

namespace Aviscribe.Capture;

internal sealed class FlashCapVideoCapture : IVideoCapture
{
    private readonly CaptureDeviceDescriptor _descriptor;
    private readonly VideoCharacteristics _characteristics;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private CaptureDevice? _captureDevice;
    private CancellationTokenSource? _runCancellation;
    private Task? _monitorTask;
    private int _state = (int)CaptureState.Stopped;
    private int _disposed;
    private long _sequenceNumber;

    public FlashCapVideoCapture(
        VideoDevice device,
        VideoFormat selectedFormat,
        CaptureDeviceDescriptor descriptor,
        VideoCharacteristics characteristics)
    {
        Device = device;
        SelectedFormat = selectedFormat;
        _descriptor = descriptor;
        _characteristics = characteristics;
    }

    public event Action<VideoFrame>? FrameReceived;
    public event EventHandler<CaptureStateChangedEventArgs>? StateChanged;
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public VideoDevice Device { get; }
    public VideoFormat SelectedFormat { get; }
    public CaptureState State =>
        (CaptureState)Volatile.Read(ref _state);

    public async Task StartAsync(
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(
            Volatile.Read(ref _disposed) != 0,
            this);

        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State == CaptureState.Running)
                return;

            SetState(CaptureState.Starting);
            try
            {
                _captureDevice = await _descriptor.OpenAsync(
                    _characteristics,
                    TranscodeFormats.Auto,
                    isScattering: false,
                    maxQueuingFrames: 1,
                    OnPixelBuffer,
                    cancellationToken).ConfigureAwait(false);

                await _captureDevice
                    .StartAsync(cancellationToken)
                    .ConfigureAwait(false);

                _runCancellation?.Dispose();
                _runCancellation = new CancellationTokenSource();
                SetState(CaptureState.Running);
                _monitorTask = MonitorDeviceAsync(_runCancellation.Token);
            }
            catch (Exception ex)
            {
                await DisposeCaptureDeviceAsync().ConfigureAwait(false);
                SetState(CaptureState.Faulted);
                RaiseError(
                    $"Could not start {Device.Name}: {ex.Message}",
                    ex);
                throw;
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    public async Task StopAsync(
        CancellationToken cancellationToken = default)
    {
        await _lifecycleGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (State is CaptureState.Stopped or CaptureState.Disposed)
                return;

            SetState(CaptureState.Stopping);
            _runCancellation?.Cancel();

            try
            {
                if (_captureDevice?.IsRunning == true)
                {
                    await _captureDevice
                        .StopAsync(cancellationToken)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                RaiseError(
                    $"Capture stopped with an error: {ex.Message}",
                    ex);
            }
            finally
            {
                await DisposeCaptureDeviceAsync().ConfigureAwait(false);
                SetState(CaptureState.Stopped);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }

        if (_monitorTask != null)
        {
            try
            {
                await _monitorTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            _monitorTask = null;
        }
    }

    private void OnPixelBuffer(PixelBufferScope scope)
    {
        if (State != CaptureState.Running)
            return;

        try
        {
            var image = scope.Buffer.CopyImage();
            var decoded = Cv2.ImDecode(image, ImreadModes.Color);
            if (decoded.Empty())
            {
                decoded.Dispose();
                throw new InvalidDataException(
                    "FlashCap returned a frame that OpenCV could not decode.");
            }

            var frame = new VideoFrame(
                decoded,
                DateTime.UtcNow,
                Interlocked.Increment(ref _sequenceNumber));
            DispatchFrame(frame);
        }
        catch (Exception ex)
        {
            RaiseError($"Could not decode a captured frame: {ex.Message}", ex);
        }
    }

    private void DispatchFrame(VideoFrame frame)
    {
        var handlers = FrameReceived?
            .GetInvocationList()
            .Cast<Action<VideoFrame>>()
            .ToArray();
        if (handlers == null || handlers.Length == 0)
        {
            frame.Dispose();
            return;
        }

        for (var index = 0; index < handlers.Length; index++)
        {
            var delivered = index == handlers.Length - 1
                ? frame
                : frame.Clone();
            try
            {
                handlers[index](delivered);
            }
            catch (Exception ex)
            {
                delivered.Dispose();
                RaiseError(
                    $"A frame consumer failed: {ex.Message}",
                    ex);
            }
        }
    }

    private async Task MonitorDeviceAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken)
                .ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                var captureDevice = _captureDevice;
                if (State == CaptureState.Running &&
                    captureDevice != null &&
                    !captureDevice.IsRunning)
                {
                    SetState(CaptureState.Faulted);
                    RaiseError(
                        $"{Device.Name} disconnected or stopped responding.",
                        deviceDisconnected: true);
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task DisposeCaptureDeviceAsync()
    {
        var captureDevice = _captureDevice;
        _captureDevice = null;
        if (captureDevice != null)
            await captureDevice.DisposeAsync().ConfigureAwait(false);

        _runCancellation?.Dispose();
        _runCancellation = null;
    }

    private void SetState(CaptureState state)
    {
        var previous = (CaptureState)Interlocked.Exchange(
            ref _state,
            (int)state);
        if (previous == state)
            return;

        try
        {
            StateChanged?.Invoke(
                this,
                new CaptureStateChangedEventArgs(previous, state));
        }
        catch
        {
        }
    }

    private void RaiseError(
        string message,
        Exception? exception = null,
        bool deviceDisconnected = false)
    {
        try
        {
            CaptureFailed?.Invoke(
                this,
                new CaptureErrorEventArgs(
                    message,
                    exception,
                    deviceDisconnected));
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        await StopAsync().ConfigureAwait(false);
        SetState(CaptureState.Disposed);
        _lifecycleGate.Dispose();
    }
}
