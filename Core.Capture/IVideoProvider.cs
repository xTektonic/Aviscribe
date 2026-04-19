using System.Collections.Generic;

namespace Aviscribe.Core.Capture
{
    public interface IVideoProvider
    {
        IReadOnlyList<VideoDevice> GetDevices();
        IVideoCapture GetVideoCapture(string deviceId);
    }
}