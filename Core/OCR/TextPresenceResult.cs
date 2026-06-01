namespace Aviscribe.Core.Ocr
{
    public readonly record struct TextPresenceResult(
        bool Present,
        double Confidence,
        string DetectorName)
    {
        public static TextPresenceResult Absent(string detectorName)
        {
            return new TextPresenceResult(false, 0, detectorName);
        }

        public static TextPresenceResult PresentResult(string detectorName, double confidence = 1)
        {
            return new TextPresenceResult(true, confidence, detectorName);
        }
    }
}
