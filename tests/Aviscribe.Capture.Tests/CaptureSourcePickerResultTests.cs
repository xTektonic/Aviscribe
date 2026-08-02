using Aviscribe.Core.Capture;
using Aviscribe.UI;

namespace Aviscribe.Capture.Tests;

public sealed class CaptureSourcePickerResultTests
{
    [Fact]
    public async Task PreparedCaptureCanBeTransferredOnlyOnce()
    {
        var capture = new TrackingCapture();
        await using var result = new CaptureSourcePickerResult(
            capture.Device,
            capture);

        Assert.Same(capture, result.TakePreparedCapture());
        Assert.Null(result.TakePreparedCapture());

        await result.DisposeAsync();
        Assert.Equal(0, capture.DisposeCount);
        await capture.DisposeAsync();
    }

    [Fact]
    public async Task DisposesPreparedCaptureWhenOwnershipIsNotTransferred()
    {
        var capture = new TrackingCapture();
        var result = new CaptureSourcePickerResult(capture.Device, capture);

        await result.DisposeAsync();
        await result.DisposeAsync();

        Assert.Equal(1, capture.DisposeCount);
    }

    private sealed class TrackingCapture : IVideoCapture
    {
        public event Action<VideoFrame>? FrameReceived { add { } remove { } }
        public event EventHandler<CaptureStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<CaptureErrorEventArgs>? CaptureFailed { add { } remove { } }

        public VideoDevice Device { get; } = new()
        {
            Id = "test:interactive",
            Name = "Interactive test source",
            Capabilities = [new VideoFormat(640, 360, "BGR", 30, 1)]
        };
        public VideoFormat SelectedFormat => Device.Capabilities[0];
        public CaptureState State => CaptureState.Stopped;
        public int DisposeCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task StopAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public void Dispose() => DisposeCount++;

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
