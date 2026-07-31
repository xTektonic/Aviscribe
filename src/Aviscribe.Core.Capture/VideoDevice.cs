using System.Collections.Generic;

namespace Aviscribe.Core.Capture
{
    public sealed class VideoDevice
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Backend { get; set; } = "";
        public IReadOnlyList<VideoFormat> Capabilities { get; set; } = [];

        public override string ToString() => Name;
    }
}
