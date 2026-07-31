#if WINDOWS_DIRECTSHOW_FALLBACK
using Accord.Video;
using Accord.Video.DirectShow;
using Aviscribe.Core.Capture;
using OpenCvSharp;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;

namespace Aviscribe.Capture;

[SupportedOSPlatform("windows")]
internal sealed class WindowsDirectShowFallbackCapture : IVideoCapture
{
    private readonly VideoCaptureDevice _source;
    private readonly SemaphoreSlim _lifecycleGate = new(1, 1);
    private int _state = (int)CaptureState.Stopped;
    private int _disposed;
    private long _sequenceNumber;

    public WindowsDirectShowFallbackCapture(
        VideoDevice device,
        VideoFormat selectedFormat,
        string moniker,
        VideoCapabilities? capability)
    {
        Device = device;
        SelectedFormat = selectedFormat;
        _source = new VideoCaptureDevice(moniker);
        if (capability != null)
            _source.VideoResolution = capability;
    }

    public event Action<VideoFrame>? FrameReceived;
    public event EventHandler<CaptureStateChangedEventArgs>? StateChanged;
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public VideoDevice Device { get; }
    public VideoFormat SelectedFormat { get; }
    public CaptureState State => (CaptureState)Volatile.Read(ref _state);

    public async Task StartAsync(CancellationToken cancellationToken = default)
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
            Subscribe();
            try
            {
                _source.Start();
                SetState(CaptureState.Running);
            }
            catch (Exception ex)
            {
                Unsubscribe();
                SetState(CaptureState.Faulted);
                RaiseError(
                    $"Could not start {Device.Name} through the DirectShow " +
                    $"compatibility path: {ex.Message}",
                    ex);
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
            try
            {
                if (_source.IsRunning)
                {
                    _source.SignalToStop();
                    await Task.Run(
                        _source.WaitForStop,
                        cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                RaiseError(
                    $"DirectShow capture stopped with an error: {ex.Message}",
                    ex);
            }
            finally
            {
                Unsubscribe();
                SetState(CaptureState.Stopped);
            }
        }
        finally
        {
            _lifecycleGate.Release();
        }
    }

    private void Subscribe()
    {
        _source.NewFrame += OnNewFrame;
        _source.VideoSourceError += OnVideoSourceError;
        _source.PlayingFinished += OnPlayingFinished;
    }

    private void Unsubscribe()
    {
        _source.NewFrame -= OnNewFrame;
        _source.VideoSourceError -= OnVideoSourceError;
        _source.PlayingFinished -= OnPlayingFinished;
    }

    private void OnNewFrame(object sender, NewFrameEventArgs eventArgs)
    {
        if (State != CaptureState.Running)
            return;

        try
        {
            using var bgr = CopyAsBgr(eventArgs.Frame);
            var frame = new VideoFrame(
                bgr.Clone(),
                DateTime.UtcNow,
                Interlocked.Increment(ref _sequenceNumber));
            DispatchFrame(frame);
        }
        catch (Exception ex)
        {
            RaiseError(
                $"Could not convert a DirectShow frame: {ex.Message}",
                ex);
        }
    }

    private static Mat CopyAsBgr(Bitmap frame)
    {
        Bitmap? converted = null;
        var source = frame;
        if (source.PixelFormat != DrawingPixelFormat.Format24bppRgb)
        {
            converted = new Bitmap(
                source.Width,
                source.Height,
                DrawingPixelFormat.Format24bppRgb);
            using var graphics = Graphics.FromImage(converted);
            graphics.DrawImageUnscaled(source, 0, 0);
            source = converted;
        }

        try
        {
            var bounds = new Rectangle(0, 0, source.Width, source.Height);
            var data = source.LockBits(
                bounds,
                ImageLockMode.ReadOnly,
                DrawingPixelFormat.Format24bppRgb);
            try
            {
                using var view = Mat.FromPixelData(
                    source.Height,
                    source.Width,
                    MatType.CV_8UC3,
                    data.Scan0,
                    data.Stride);
                return view.Clone();
            }
            finally
            {
                source.UnlockBits(data);
            }
        }
        finally
        {
            converted?.Dispose();
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

    private void OnVideoSourceError(
        object sender,
        VideoSourceErrorEventArgs eventArgs)
    {
        if (State is CaptureState.Stopping or CaptureState.Stopped)
            return;

        SetState(CaptureState.Faulted);
        RaiseError(
            $"{Device.Name} reported a DirectShow error: " +
            eventArgs.Description);
    }

    private void OnPlayingFinished(
        object sender,
        ReasonToFinishPlaying reason)
    {
        if (State != CaptureState.Running)
            return;

        SetState(CaptureState.Faulted);
        RaiseError(
            $"{Device.Name} disconnected or stopped ({reason}).",
            deviceDisconnected: reason != ReasonToFinishPlaying.StoppedByUser);
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
        _source.Stop();
        SetState(CaptureState.Disposed);
        _lifecycleGate.Dispose();
    }
}
#endif
