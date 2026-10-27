using OpenCvSharp;
using SMOVision.Core;
using SMOVision.Core.Capture;
using System.Runtime.InteropServices;
namespace Aviscribe.Core.Capture;

/// <summary>App frame lease. OpenCV pixels are materialized only for a requested UI image.</summary>
public sealed class VideoFrame : IDisposable
{
    private FrameBuffer? _buffer;
    private Mat? _image;
    private int _disposed;
    public DateTime Timestamp { get; }
    public long SequenceNumber { get; }
    public bool IsDisposed => Volatile.Read(ref _disposed) != 0;
    public int Width => _buffer?.Width ?? Frame.Width;
    public int Height => _buffer?.Height ?? Frame.Height;
    public Mat Frame
    {
        get
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_image is not null) return _image;
            var buffer = _buffer!;
            var image = new Mat(buffer.Height, buffer.Width, buffer.Format == SMOVision.Core.Capture.PixelFormat.Bgr24 ? MatType.CV_8UC3 : MatType.CV_8UC4);
            var pixels = buffer.Pixels.ToArray();
            for (var y = 0; y < buffer.Height; y++)
                Marshal.Copy(pixels, y * buffer.Stride, image.Ptr(y), buffer.Width * buffer.BytesPerPixel);
            return _image = image;
        }
    }
    public VideoFrame(Mat frame, DateTime timestamp, long sequenceNumber = 0)
    { _image = frame ?? throw new ArgumentNullException(nameof(frame)); Timestamp = timestamp; SequenceNumber = sequenceNumber; }
    public VideoFrame(FrameBuffer ownedBuffer)
    { _buffer = ownedBuffer; Timestamp = DateTime.UnixEpoch + ownedBuffer.Stamp.Timestamp; SequenceNumber = ownedBuffer.Stamp.Sequence; }
    public FrameBuffer RetainBuffer(FrameStamp? stamp = null)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        if (_buffer is not null) return _buffer.Retain(stamp);
        var image = Frame;
        var height = image.Height;
        var stride = checked(image.Width * image.Channels());
        var pixels = new byte[checked(stride * height)];
        for (var y = 0; y < height; y++) Marshal.Copy(image.Ptr(y), pixels, y * stride, stride);
        return FrameBuffer.Copy(pixels, image.Width, image.Height, stride,
            image.Channels() == 3 ? SMOVision.Core.Capture.PixelFormat.Bgr24 : SMOVision.Core.Capture.PixelFormat.Bgra32,
            stamp ?? new FrameStamp("gameplay", Guid.Empty, 0, SequenceNumber, Timestamp - DateTime.UnixEpoch));
    }
    public VideoFrame Clone() => new(RetainBuffer());
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _image?.Dispose(); _buffer?.Dispose();
    }
}
