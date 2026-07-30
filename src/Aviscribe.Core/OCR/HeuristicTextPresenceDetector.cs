using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public class HeuristicTextPresenceDetector : ITextPresenceDetector
    {
        public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
        {
            var present = regionType switch
            {
                OcrRegionType.Talkatoo => TextDetection.HasTalkatooText(image),
                OcrRegionType.MoonGet => TextDetection.HasMoonText(image),
                OcrRegionType.StoryMoon => TextDetection.HasStoryMoonText(image),
                _ => false
            };

            return present
                ? TextPresenceResult.PresentResult(nameof(HeuristicTextPresenceDetector))
                : TextPresenceResult.Absent(nameof(HeuristicTextPresenceDetector));
        }
    }
}
