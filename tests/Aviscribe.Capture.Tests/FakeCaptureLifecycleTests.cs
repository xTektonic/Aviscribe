using Aviscribe.Core.Capture;
using OpenCvSharp;

namespace Aviscribe.Capture.Tests;

public sealed class FakeCaptureLifecycleTests
{
    [Fact]
    public async Task EmptyDeviceListCanBeRefreshedAndOpenedLater()
    {
        var fakeProvider = new FakeVideoProvider();
        IVideoProvider provider = fakeProvider;
        Assert.Empty(await provider.RefreshAsync(
            TestContext.Current.CancellationToken));

        fakeProvider.Devices =
        [
            new VideoDevice
            {
                Id = "fake:device:1",
                Name = "Fake capture",
                Backend = "Fake",
                Capabilities =
                [
                    new VideoFormat(1920, 1080, "BGR", 60, 1)
                ]
            }
        ];

        var devices = await provider.RefreshAsync(
            TestContext.Current.CancellationToken);
        await using var capture = await provider.OpenCaptureAsync(
            devices[0].Id,
            cancellationToken: TestContext.Current.CancellationToken);

        await capture.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CaptureState.Running, capture.State);
    }

    [Fact]
    public async Task DeviceLossStopsSnapshotAndCaptureCanRestart()
    {
        IVideoProvider provider = FakeVideoProvider.WithDevice();
        await using var capture = await provider.OpenCaptureAsync(
            "fake:device:1",
            cancellationToken: TestContext.Current.CancellationToken);
        using var broker = new RawFrameSnapshotBroker();
        capture.FrameReceived += frame =>
        {
            broker.Offer(frame);
            frame.Dispose();
        };
        capture.CaptureFailed += (_, error) =>
            broker.Cancel(error.Exception ?? new IOException(error.Message));

        await capture.StartAsync(TestContext.Current.CancellationToken);
        var pending = broker.RequestAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        ((FakeVideoCapture)capture).LoseDevice();

        await Assert.ThrowsAsync<IOException>(() => pending);
        Assert.Equal(CaptureState.Faulted, capture.State);

        await capture.StopAsync(TestContext.Current.CancellationToken);
        await capture.StartAsync(TestContext.Current.CancellationToken);

        Assert.Equal(CaptureState.Running, capture.State);
        Assert.Equal(2, ((FakeVideoCapture)capture).StartCount);
    }

    private sealed class FakeVideoProvider : IVideoProvider
    {
        public IReadOnlyList<VideoDevice> Devices { get; set; } = [];

        public static FakeVideoProvider WithDevice()
        {
            return new FakeVideoProvider
            {
                Devices =
                [
                    new VideoDevice
                    {
                        Id = "fake:device:1",
                        Name = "Fake capture",
                        Backend = "Fake",
                        Capabilities =
                        [
                            new VideoFormat(1920, 1080, "BGR", 60, 1)
                        ]
                    }
                ]
            };
        }

        public IReadOnlyList<VideoDevice> GetDevices() => Devices;

        public IVideoCapture GetVideoCapture(
            string deviceId,
            string? formatId = null)
        {
            var device = Devices.SingleOrDefault(item =>
                item.Id == deviceId);
            if (device == null)
                throw new InvalidOperationException("Device unavailable.");
            return new FakeVideoCapture(device);
        }
    }

    private sealed class FakeVideoCapture : IVideoCapture
    {
        public FakeVideoCapture(VideoDevice device)
        {
            Device = device;
            SelectedFormat = device.Capabilities[0];
        }

        public event Action<VideoFrame>? FrameReceived;
        public event EventHandler<CaptureStateChangedEventArgs>? StateChanged;
        public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

        public VideoDevice Device { get; }
        public VideoFormat SelectedFormat { get; }
        public CaptureState State { get; private set; }
        public int StartCount { get; private set; }

        public Task StartAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StartCount++;
            SetState(CaptureState.Running);
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SetState(CaptureState.Stopped);
            return Task.CompletedTask;
        }

        public void LoseDevice()
        {
            SetState(CaptureState.Faulted);
            CaptureFailed?.Invoke(
                this,
                new CaptureErrorEventArgs(
                    "device disconnected",
                    new IOException("device disconnected"),
                    deviceDisconnected: true));
        }

        public void EmitFrame()
        {
            FrameReceived?.Invoke(new VideoFrame(
                new Mat(1080, 1920, MatType.CV_8UC3),
                DateTime.UtcNow));
        }

        private void SetState(CaptureState state)
        {
            var previous = State;
            State = state;
            StateChanged?.Invoke(
                this,
                new CaptureStateChangedEventArgs(previous, state));
        }

        public void Dispose()
        {
            SetState(CaptureState.Disposed);
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
