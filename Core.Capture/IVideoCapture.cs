using System;

namespace Aviscribe.Core.Capture
{
    public interface IVideoCapture
    {
        event Action<VideoFrame> FrameReceived;

        void Start();
        void Stop();
    }
}