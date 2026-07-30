using Aviscribe.Core.Capture;
using System;
using System.Collections.Generic;

namespace Aviscribe.UI
{
    internal class DesignVideoProvider : IVideoProvider
    {
        public IReadOnlyList<VideoDevice> GetDevices()
        {
            return Array.Empty<VideoDevice>();
        }

        public IVideoCapture GetVideoCapture(string deviceId)
        {
            throw new InvalidOperationException("Design-time capture provider cannot create video captures.");
        }
    }
}
