using System.Collections.Generic;
using Aviscribe.Core.Capture;

namespace Aviscribe.Core
{
    public sealed class SavedRunState
    {
        public string CurrentKingdom { get; set; } = string.Empty;
        public RunSettings Settings { get; set; } = new();
        public bool WriteOverlay { get; set; } = true;
        public string OverlayOutputPath { get; set; } = AppPaths.PendingOutputPath;
        public string CaptureDeviceId { get; set; } = string.Empty;
        public Dictionary<string, CaptureCropSettings> CaptureCropsByDevice { get; set; } = new();
        public List<int> PendingMoonIds { get; set; } = new();
        public List<int> CollectedMoonIds { get; set; } = new();
        public List<int> UncountedCollectedMoonIds { get; set; } = new();
    }
}
