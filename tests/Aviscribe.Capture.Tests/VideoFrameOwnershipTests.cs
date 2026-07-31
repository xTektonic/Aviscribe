using Aviscribe.Core.Capture;
using OpenCvSharp;

namespace Aviscribe.Capture.Tests;

public sealed class VideoFrameOwnershipTests
{
    [Fact]
    public void CloneOwnsIndependentPixels()
    {
        var source = new Mat(2, 2, MatType.CV_8UC3, new Scalar(1, 2, 3));
        var original = new VideoFrame(source, DateTime.UtcNow, 42);
        using var clone = original.Clone();

        original.Dispose();

        Assert.True(original.IsDisposed);
        Assert.False(clone.IsDisposed);
        Assert.Equal(42, clone.SequenceNumber);
        Assert.Equal(new Vec3b(1, 2, 3), clone.Frame.At<Vec3b>(0, 0));
    }

    [Fact]
    public void DisposeIsIdempotent()
    {
        var frame = new VideoFrame(
            new Mat(1, 1, MatType.CV_8UC3),
            DateTime.UtcNow);

        frame.Dispose();
        frame.Dispose();

        Assert.True(frame.IsDisposed);
        Assert.Throws<ObjectDisposedException>(() => frame.Clone());
    }
}
