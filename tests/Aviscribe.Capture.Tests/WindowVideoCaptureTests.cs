using Aviscribe.Core.Capture;
using OpenCvSharp;

namespace Aviscribe.Capture.Tests;

public sealed class WindowVideoCaptureTests
{
    [Fact]
    public async Task WindowCaptureUsesExistingLifecycleAndOwnedFrameContract()
    {
        var backend = new FakeWindowBackend();
        await using var capture = new WindowVideoCapture(backend, backend.Target);
        var received = new TaskCompletionSource<VideoFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.FrameReceived += frame => received.TrySetResult(frame);

        await capture.StartAsync(TestContext.Current.CancellationToken);
        using var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await capture.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CaptureState.Stopped, capture.State);
        Assert.Equal(CaptureSourceKind.Window, capture.Device.Kind);
        Assert.Equal(640, frame.Frame.Width);
        Assert.False(frame.IsDisposed);
    }

    [Fact]
    public void UnavailableBackendEntryExplainsFallbackInsteadOfReturningEmptyList()
    {
        const string reason = "Use an XWayland session or a Video Device source.";
        var backend = new UnsupportedWindowCaptureBackend(reason);
        var provider = new WindowCaptureProvider(backend);

        var source = Assert.Single(provider.GetDevices());
        var error = Assert.Throws<InvalidOperationException>(() => provider.GetVideoCapture(source.Id));

        Assert.False(source.IsAvailable);
        Assert.Contains("XWayland", source.Name);
        Assert.Equal(reason, error.Message);
    }

    [Fact]
    public async Task RepeatedNativeFailuresFaultCaptureAndRaiseFriendlyError()
    {
        var backend = new ThrowingWindowBackend();
        await using var capture = new WindowVideoCapture(backend, backend.Target);
        var failed = new TaskCompletionSource<CaptureErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.CaptureFailed += (_, error) => failed.TrySetResult(error);

        await capture.StartAsync(TestContext.Current.CancellationToken);
        var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(CaptureState.Faulted, capture.State);
        Assert.True(error.DeviceDisconnected);
        Assert.Contains("Fake game", error.Message);
    }

    private sealed class FakeWindowBackend : IWindowCaptureBackend
    {
        public string Name => "Fake window";
        public WindowCaptureTarget Target { get; } =
            new("window:fake", "Fake game", 1, 640, 360);
        public IReadOnlyList<WindowCaptureTarget> EnumerateTargets() => [Target];
        public Mat Capture(WindowCaptureTarget target) =>
            new(360, 640, MatType.CV_8UC3, Scalar.All(42));
    }

    private sealed class ThrowingWindowBackend : IWindowCaptureBackend
    {
        public string Name => "Failing window";
        public WindowCaptureTarget Target { get; } =
            new("window:failing", "Fake game", 2, 640, 360);
        public IReadOnlyList<WindowCaptureTarget> EnumerateTargets() => [Target];
        public Mat Capture(WindowCaptureTarget target) =>
            throw new IOException("window unavailable");
    }
}
