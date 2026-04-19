using OpenCvSharp;
using System;

namespace Aviscribe.Core.Capture
{
    public class VideoFrame
    {
        public Mat Frame { get; }
        public DateTime Timestamp { get; }

        public VideoFrame(Mat frame, DateTime timestamp)
        {
            Frame = frame;
            Timestamp = timestamp;
        }
    }
}