using System.Collections.Generic;

namespace Aviscribe.Core.Capture
{
    public sealed class VideoDevice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Backend { get; set; } = "";
        public CaptureSourceKind Kind { get; set; } = CaptureSourceKind.VideoDevice;
        public bool IsAvailable { get; set; } = true;
        public string UnavailableReason { get; set; } = "";
        public IReadOnlyList<VideoFormat> Capabilities { get; set; } = [];

        public override string ToString() => Name;
    }
}
