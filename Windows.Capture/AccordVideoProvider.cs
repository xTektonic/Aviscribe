using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aviscribe.Core.Capture;
using Accord.Video.DirectShow;

namespace Aviscribe.Windows.Capture
{
    public class AccordVideoProvider : IVideoProvider
    {
        public IReadOnlyList<VideoDevice> GetDevices()
        {
            // Enumerate all video devices
            FilterInfoCollection videoDevices = new FilterInfoCollection(FilterCategory.VideoInputDevice);
            return videoDevices.Select(x => new VideoDevice
            {
                Id = x.MonikerString,
                Name = x.Name
            }).ToList();
        }

        public IVideoCapture GetVideoCapture(string deviceMoniker)
        {
            return new AccordVideoCapture(deviceMoniker);
        }
    }
}
