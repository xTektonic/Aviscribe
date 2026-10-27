using System.Runtime.CompilerServices;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Integration;
using SMOVision.Capture;
using SMOVision.Core;
using SMOVision.Core.Capture;

namespace Aviscribe.Capture.Tests;

public sealed class SmoCaptureTests
{
    [Theory]
    [InlineData(SMOVision.Capture.CaptureSourceKind.Window, Aviscribe.Core.Capture.CaptureSourceKind.Window)]
    [InlineData(SMOVision.Capture.CaptureSourceKind.VideoDevice, Aviscribe.Core.Capture.CaptureSourceKind.VideoDevice)]
    public void SourceMappingPreservesLegacyKindsAndOpaqueFormatIds(SMOVision.Capture.CaptureSourceKind kind, Aviscribe.Core.Capture.CaptureSourceKind expected)
    {
        var source = new CaptureSource("stable-id", "Test", "Backend", kind, false, "Permission required", true,
            [new CaptureFormat("opaque-format", 64, 36, "BGR24", 60, 1)]);
        var mapped = SmoVideoProvider.ConvertSource(source);
        Assert.Equal(expected, mapped.Kind); Assert.Equal("opaque-format", Assert.Single(mapped.Capabilities).Id);
        Assert.False(mapped.IsAvailable); Assert.True(mapped.RequiresInteractiveSelection);
        Assert.Equal("Permission required", mapped.UnavailableReason);
    }

    [Fact]
    public async Task CaptureStartsStopsAndReopensWithoutInvalidatingRetainedFrames()
    {
        var first = new FakeSource(); var second = new FakeSource(); var reopened = 0;
        await using var capture = new SmoVideoCapture(new VideoDevice { Id = "test" }, null, first,
            _ => { reopened++; return ValueTask.FromResult<IFrameSource>(second); });
        VideoFrame? retained = null;
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.FrameReceived += frame => { retained?.Dispose(); retained = frame; received.TrySetResult(); };
        await capture.StartAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(CaptureState.Running, capture.State);
        Assert.Equal(64, retained!.Width);
        await capture.StopAsync(TestContext.Current.CancellationToken);
        Assert.True(first.Disposed); Assert.Equal(64, retained.Width);
        using var retainedBuffer = retained.RetainBuffer();
        Assert.Equal(64 * 36 * 3, retainedBuffer.Pixels.Length);
        received = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await capture.StartAsync(TestContext.Current.CancellationToken);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(1, reopened); Assert.Equal(1, first.Reads); Assert.Equal(1, second.Reads);
        await capture.StopAsync(TestContext.Current.CancellationToken); retained?.Dispose();
    }

    [Fact]
    public async Task CaptureUsesActualFrameDimensionsAfterResize()
    {
        var source = new PushFrameSource("resize", ProcessingMode.Live);
        await using var capture = new SmoVideoCapture(new VideoDevice(), null, source, _ => throw new NotSupportedException());
        var resized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.FrameReceived += frame =>
        {
            using (frame) if (frame.Width == 128 && frame.Height == 72) resized.TrySetResult();
        };
        var token = TestContext.Current.CancellationToken;
        var start = capture.StartAsync(token);
        await source.SubmitOwnedAsync(FrameBuffer.Copy(new byte[64 * 36 * 3], 64, 36, 64 * 3,
            SMOVision.Core.Capture.PixelFormat.Bgr24, new("resize", Guid.Empty, 0, 0, TimeSpan.Zero)), token);
        await start.WaitAsync(TimeSpan.FromSeconds(5), token);
        await source.SubmitOwnedAsync(FrameBuffer.Copy(new byte[128 * 72 * 3], 128, 72, 128 * 3,
            SMOVision.Core.Capture.PixelFormat.Bgr24, new("resize", Guid.Empty, 0, 1, TimeSpan.FromSeconds(1))), token);
        await resized.Task.WaitAsync(TimeSpan.FromSeconds(5), token);
        Assert.Equal(128, capture.SelectedFormat.Width); Assert.Equal(72, capture.SelectedFormat.Height);
        await capture.StopAsync(token);
    }

    [Fact]
    public async Task SourceLossIsReportedAndDisposed()
    {
        var source = new FakeSource { Fail = true };
        await using var capture = new SmoVideoCapture(new VideoDevice(), null, source, _ => throw new NotSupportedException());
        var failed = new TaskCompletionSource<CaptureErrorEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        capture.FrameReceived += frame => frame.Dispose();
        capture.CaptureFailed += (_, error) => failed.TrySetResult(error);
        await capture.StartAsync(TestContext.Current.CancellationToken);
        var failure = await failed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Contains("lost", failure.Message); Assert.Equal(CaptureState.Faulted, capture.State);
        await capture.StopAsync(TestContext.Current.CancellationToken); Assert.True(source.Disposed);
    }

    [Fact]
    public async Task CancellingStartupWithoutFramesCompletesCleanup()
    {
        var source = new FakeSource { NoFrames = true };
        await using var capture = new SmoVideoCapture(new VideoDevice(), null, source, _ => throw new NotSupportedException());
        using var cancel = new CancellationTokenSource();
        var starting = capture.StartAsync(cancel.Token);
        await source.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => starting.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(source.Disposed);
    }

    [Fact]
    public void LeasedPaddedRowsSurviveOriginalDisposal()
    {
        var pixels = new byte[] { 1, 2, 3, 99, 4, 5, 6, 99 };
        var buffer = FrameBuffer.Copy(pixels, 1, 2, 4, SMOVision.Core.Capture.PixelFormat.Bgr24,
            new("test", Guid.Empty, 0, 1, TimeSpan.Zero));
        var original = new VideoFrame(buffer); using var retained = original.Clone(); original.Dispose();
        Assert.Equal(new OpenCvSharp.Vec3b(4, 5, 6), retained.Frame.At<OpenCvSharp.Vec3b>(1, 0));
    }

    private sealed class FakeSource : IFrameSource
    {
        public bool Fail { get; init; }
        public bool NoFrames { get; init; }
        public bool Disposed { get; private set; }
        public int Reads { get; private set; }
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public FrameSourceInfo Info => new("test", true);
        public async IAsyncEnumerable<FrameBuffer> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Reads++; Entered.TrySetResult();
            if (!NoFrames) yield return FrameBuffer.Copy(new byte[64 * 36 * 3], 64, 36, 64 * 3,
                SMOVision.Core.Capture.PixelFormat.Bgr24, new FrameStamp("test", Guid.Empty, 0, 0, TimeSpan.Zero));
            if (Fail) throw new IOException("Source lost");
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
        }
        public ValueTask DisposeAsync() { Disposed = true; return ValueTask.CompletedTask; }
    }
}
