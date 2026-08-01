using Aviscribe.Core.Capture;

namespace Aviscribe.Capture.Tests;

public sealed class CompositeVideoProviderTests
{
    [Fact]
    public async Task RefreshCombinesSourcesAndDispatchesOpenToOwner()
    {
        var camera = Source("camera:1", CaptureSourceKind.VideoDevice);
        var window = Source("window:1", CaptureSourceKind.Window);
        var cameraProvider = new StubProvider(camera);
        var windowProvider = new StubProvider(window);
        IVideoProvider provider = new CompositeVideoProvider(cameraProvider, windowProvider);

        var sources = await provider.RefreshAsync(TestContext.Current.CancellationToken);
        await using var capture = await provider.OpenCaptureAsync(
            window.Id,
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal([camera.Id, window.Id], sources.Select(source => source.Id));
        Assert.Equal(window.Id, capture.Device.Id);
        Assert.Equal(0, cameraProvider.OpenCount);
        Assert.Equal(1, windowProvider.OpenCount);
    }

    private static VideoDevice Source(string id, CaptureSourceKind kind) =>
        new()
        {
            Id = id,
            Name = id,
            Kind = kind,
            Capabilities = [new VideoFormat(640, 360, "BGR", 10, 1)]
        };

    private sealed class StubProvider(VideoDevice source) : IVideoProvider
    {
        public int OpenCount { get; private set; }
        public IReadOnlyList<VideoDevice> GetDevices() => [source];
        public IVideoCapture GetVideoCapture(string deviceId, string? formatId = null)
        {
            OpenCount++;
            return new StubCapture(source);
        }
    }

    private sealed class StubCapture(VideoDevice source) : IVideoCapture
    {
        public event Action<VideoFrame>? FrameReceived { add { } remove { } }
        public event EventHandler<CaptureStateChangedEventArgs>? StateChanged { add { } remove { } }
        public event EventHandler<CaptureErrorEventArgs>? CaptureFailed { add { } remove { } }
        public VideoDevice Device => source;
        public VideoFormat SelectedFormat => source.Capabilities[0];
        public CaptureState State { get; private set; }
        public Task StartAsync(CancellationToken cancellationToken = default) { State = CaptureState.Running; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken = default) { State = CaptureState.Stopped; return Task.CompletedTask; }
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
