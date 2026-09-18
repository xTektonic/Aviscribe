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
        capture.FrameReceived += frame => { if (!received.TrySetResult(frame)) frame.Dispose(); };

        await capture.StartAsync(TestContext.Current.CancellationToken);
        using var frame = await received.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        await capture.StopAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CaptureState.Stopped, capture.State);
        Assert.Equal(CaptureSourceKind.Window, capture.Device.Kind);
        Assert.Equal(60, capture.SelectedFormat.FramesPerSecond);
        Assert.Equal(
            60,
            Assert.Single(capture.Device.Capabilities).FramesPerSecond);
        Assert.Equal(640, frame.Frame.Width);
        Assert.False(frame.IsDisposed);
    }

    [Fact]
    public void AvailableWindowSourcesAdvertiseSixtyFramesPerSecond()
    {
        var backend = new FakeWindowBackend();
        var provider = new WindowCaptureProvider(backend);

        var source = Assert.Single(provider.GetDevices());
        var format = Assert.Single(source.Capabilities);

        Assert.Equal(60, format.FramesPerSecond);
        Assert.Equal("BGR", format.PixelFormat);
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
    public async Task FailedFirstFrameRejectsStartAndRaisesFriendlyError()
    {
        var backend = new ThrowingWindowBackend();
        await using var capture = new WindowVideoCapture(backend, backend.Target);
        var failed = new TaskCompletionSource<CaptureErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.CaptureFailed += (_, error) => failed.TrySetResult(error);

        await Assert.ThrowsAsync<IOException>(() => capture.StartAsync(TestContext.Current.CancellationToken));
        var error = await failed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);

        Assert.Equal(CaptureState.Faulted, capture.State);
        Assert.False(error.DeviceDisconnected);
        Assert.Contains("Fake game", error.Message);
    }

    [Fact]
    public async Task StartWaitsForFirstFrameAndStopDisposesNativeSession()
    {
        var backend = new SessionBackend(blockFirstFrame: true);
        await using var capture = new WindowVideoCapture(backend, backend.Target);
        var start = capture.StartAsync(TestContext.Current.CancellationToken);
        await backend.Opened.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        try
        {
            Assert.Equal(CaptureState.Starting, capture.State);
            Assert.False(start.IsCompleted);
        }
        finally { backend.AllowFrame.Set(); }
        await start;
        Assert.Equal(CaptureState.Running, capture.State);
        await capture.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.Disposals);
        await capture.StartAsync(TestContext.Current.CancellationToken);
        await capture.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, backend.Disposals);
        Assert.Equal(2, backend.Opens);
    }

    [Fact]
    public async Task CancellationDuringFirstFrameDisposesSessionWithoutReportingRunning()
    {
        var backend = new SessionBackend(blockFirstFrame: true);
        await using var capture = new WindowVideoCapture(backend, backend.Target);
        using var cancellation = new CancellationTokenSource();
        var states = new List<CaptureState>();
        capture.StateChanged += (_, args) => states.Add(args.Current);
        var start = capture.StartAsync(cancellation.Token);
        await backend.Opened.Task.WaitAsync(TimeSpan.FromSeconds(2), TestContext.Current.CancellationToken);
        cancellation.Cancel();
        backend.AllowFrame.Set();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => start);
        Assert.Equal(1, backend.Disposals);
        Assert.Equal(CaptureState.Stopped, capture.State);
        Assert.DoesNotContain(CaptureState.Running, states);
    }

    [Fact]
    public async Task FailureAfterFirstFrameDisposesSessionAndCanBeRestarted()
    {
        var backend = new SessionBackend(failAfterFirstFrame: true);
        await using var capture = new WindowVideoCapture(backend, backend.Target);
        var failed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.CaptureFailed += (_, _) => failed.TrySetResult();
        await capture.StartAsync(TestContext.Current.CancellationToken);
        await failed.Task.WaitAsync(TimeSpan.FromSeconds(3), TestContext.Current.CancellationToken);
        await capture.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, backend.Disposals);
        await capture.StartAsync(TestContext.Current.CancellationToken);
        await capture.StopAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, backend.Disposals);
    }

    private sealed class SessionBackend(bool blockFirstFrame = false, bool failAfterFirstFrame = false) : IWindowCaptureBackend
    {
        public string Name => "Session backend";
        public WindowCaptureTarget Target { get; } = new("window:session", "Session window", 3, 640, 360);
        public TaskCompletionSource Opened { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim AllowFrame { get; } = new(!blockFirstFrame);
        public int Disposals;
        public int Opens;
        public IReadOnlyList<WindowCaptureTarget> EnumerateTargets() => [Target];
        public IWindowCaptureSession OpenSession(WindowCaptureTarget target)
        {
            Interlocked.Increment(ref Opens);
            Opened.TrySetResult();
            return new Session(this, failAfterFirstFrame);
        }
        private sealed class Session(SessionBackend owner, bool fail) : IWindowCaptureSession
        {
            private int _frames;
            public Mat Capture()
            {
                if (!owner.AllowFrame.Wait(TimeSpan.FromSeconds(3))) throw new TimeoutException();
                if (fail && _frames++ > 0) throw new IOException("Stream stopped");
                return new Mat(360, 640, MatType.CV_8UC3, Scalar.All(42));
            }
            public void Dispose() => Interlocked.Increment(ref owner.Disposals);
        }
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
