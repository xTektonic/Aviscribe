using System.Collections.Generic;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;

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
        public Dictionary<string, SavedKingdomState> KingdomStates { get; set; } =
            new(System.StringComparer.OrdinalIgnoreCase);
        public List<SavedReviewState> AmbiguousReviews { get; set; } = new();
    }

    public sealed class SavedKingdomState
    {
        public List<SavedMoonReference> Pending { get; set; } = new();
        public List<SavedMoonReference> Collected { get; set; } = new();
        public List<SavedMoonReference> UncountedCollected { get; set; } = new();
    }

    public class SavedMoonReference
    {
        public string Kingdom { get; set; } = string.Empty;
        public int MoonId { get; set; }
    }

    public sealed class SavedReviewState
    {
        public string Kingdom { get; set; } = string.Empty;
        public OcrRegionType Type { get; set; }
        public string Text { get; set; } = string.Empty;
        public List<SavedReviewCandidate> Candidates { get; set; } = new();
    }

    public sealed class SavedReviewCandidate : SavedMoonReference
    {
        public double Score { get; set; }
    }

    public sealed record KingdomAmbiguousReview(
        string Kingdom,
        AmbiguousOcrResult Result);
}
