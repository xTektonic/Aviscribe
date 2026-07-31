using System;
using System.Threading;
using System.Threading.Tasks;

namespace Aviscribe.Core.Capture
{
    public interface IVideoCapture : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Delivers an owned raw, uncropped BGR source frame. The subscriber
        /// must dispose the frame or transfer ownership to another component.
        /// </summary>
        event Action<VideoFrame>? FrameReceived;
        event EventHandler<CaptureStateChangedEventArgs>? StateChanged;
        event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

        VideoDevice Device { get; }
        VideoFormat SelectedFormat { get; }
        CaptureState State { get; }

        Task StartAsync(CancellationToken cancellationToken = default);
        Task StopAsync(CancellationToken cancellationToken = default);

        void Start() => StartAsync().GetAwaiter().GetResult();
        void Stop() => StopAsync().GetAwaiter().GetResult();
    }
}
