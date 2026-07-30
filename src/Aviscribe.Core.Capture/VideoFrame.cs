using OpenCvSharp;
using System;
using System.Threading;

namespace Aviscribe.Core.Capture
{
    /// <summary>
    /// Owns one raw BGR source frame. The receiver that is handed an instance
    /// owns it and must dispose it or transfer that ownership exactly once.
    /// </summary>
    public sealed class VideoFrame : IDisposable
    {
        private int _disposed;

        public Mat Frame { get; }
        public DateTime Timestamp { get; }
        public long SequenceNumber { get; }
        public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

        public VideoFrame(Mat frame, DateTime timestamp, long sequenceNumber = 0)
        {
            Frame = frame ?? throw new ArgumentNullException(nameof(frame));
            Timestamp = timestamp;
            SequenceNumber = sequenceNumber;
        }

        public VideoFrame Clone()
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return new VideoFrame(Frame.Clone(), Timestamp, SequenceNumber);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
                Frame.Dispose();
        }
    }
}
