using Aviscribe.Core.Capture;
using OpenCvSharp;

namespace Aviscribe.Capture.Tests;

public sealed class RawFrameSnapshotBrokerTests
{
    [Fact]
    public async Task SnapshotIsRawCloneAndDoesNotConsumeCaptureFrame()
    {
        using var broker = new RawFrameSnapshotBroker();
        using var raw = new VideoFrame(
            new Mat(9, 16, MatType.CV_8UC3, new Scalar(7, 8, 9)),
            DateTime.UtcNow,
            17);
        var pending = broker.RequestAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        Assert.True(broker.Offer(raw));
        using var snapshot = await pending;

        Assert.False(raw.IsDisposed);
        Assert.Equal(16, snapshot.Frame.Width);
        Assert.Equal(9, snapshot.Frame.Height);
        Assert.Equal(17, snapshot.SequenceNumber);
        Assert.Equal(new Vec3b(7, 8, 9), snapshot.Frame.At<Vec3b>(0, 0));
    }

    [Fact]
    public async Task NewerRequestCancelsOlderRequest()
    {
        using var broker = new RawFrameSnapshotBroker();
        var older = broker.RequestAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        var newer = broker.RequestAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);
        using var raw = new VideoFrame(
            new Mat(9, 16, MatType.CV_8UC3),
            DateTime.UtcNow);

        broker.Offer(raw);
        using var snapshot = await newer;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => older);
        Assert.Equal(16, snapshot.Frame.Width);
    }

    [Fact]
    public async Task CallerCancellationCompletesCleanly()
    {
        using var broker = new RawFrameSnapshotBroker();
        using var cancellation = new CancellationTokenSource();
        var pending = broker.RequestAsync(
            TimeSpan.FromSeconds(1),
            cancellation.Token);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => pending);
    }

    [Fact]
    public async Task TimeoutHasSpecificFailure()
    {
        using var broker = new RawFrameSnapshotBroker();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            broker.RequestAsync(
                TimeSpan.FromMilliseconds(20),
                TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DeviceLossPropagatesToPendingRequest()
    {
        using var broker = new RawFrameSnapshotBroker();
        var pending = broker.RequestAsync(
            TimeSpan.FromSeconds(1),
            TestContext.Current.CancellationToken);

        broker.Cancel(new IOException("device disconnected"));

        var error = await Assert.ThrowsAsync<IOException>(() => pending);
        Assert.Contains("disconnected", error.Message);
    }
}
