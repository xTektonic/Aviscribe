using Accord.Video.DirectShow;
using Aviscribe.Core.Capture;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp.Extensions;
using Accord.IO;

namespace Aviscribe.Windows.Capture
{
    public class AccordVideoCapture : IVideoCapture
    {
        private string _moniker;
        private VideoCaptureDevice? _video;

        public event Action<VideoFrame>? FrameReceived;

        public AccordVideoCapture(string deviceMoniker)
        {
            _moniker = deviceMoniker;
            _video = new VideoCaptureDevice(_moniker);

            if (_video == null)
                throw new Exception($"Could not create video capture device for moniker: {_moniker}");
        }

        public void Start()
        {
            _video.NewFrame += OnNewFrame;
            _video.Start();
        }

        public void Stop()
        {
            _video.SignalToStop();
            _video.WaitForStop();
        }

        private void OnNewFrame(object sender, Accord.Video.NewFrameEventArgs eventArgs)
        {
            using var bitmap = (Bitmap)eventArgs.Frame.Clone();
            VideoFrame frame = new VideoFrame(BitmapConverter.ToMat(bitmap), DateTime.UtcNow);
            FrameReceived?.Invoke(frame);

            //bitmap.Save("C:\\Users\\[removed]\\Downloads\\moon_get.png");
        }
    }
}
