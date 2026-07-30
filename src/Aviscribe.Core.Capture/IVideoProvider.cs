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
    }
}
