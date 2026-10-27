using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Aviscribe.Core.Capture
{
    public interface IVideoProvider
    {
        IReadOnlyList<VideoDevice> GetDevices();
        IVideoCapture GetVideoCapture(string deviceId, string? formatId = null);

        ValueTask<IReadOnlyList<VideoDevice>> RefreshAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GetDevices());
        }

        ValueTask<IVideoCapture> OpenCaptureAsync(
            string deviceId,
            string? formatId = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(GetVideoCapture(deviceId, formatId));
        }

        ValueTask<IVideoCapture> OpenCaptureAsync(
            string deviceId,
            string? formatId,
            CaptureOpenOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(options);
            return OpenCaptureAsync(deviceId, formatId, cancellationToken);
        }
    }
}
