using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public class FeatureThresholdTextPresenceDetector : ITextPresenceDetector
    {
        private readonly Dictionary<OcrRegionType, List<FeatureThresholdRule>> _rules;

        public FeatureThresholdTextPresenceDetector(IEnumerable<FeatureThresholdRule> rules)
        {
            _rules = rules
                .GroupBy(rule => rule.RegionType)
                .ToDictionary(group => group.Key, group => group.ToList());
        }

        public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
        {
            if (!_rules.TryGetValue(regionType, out var rules) || rules.Count == 0)
                return TextPresenceResult.Absent(nameof(FeatureThresholdTextPresenceDetector));

            var features = ImageFeatureExtractor.Extract(image);
            var confidence = 1.0;

            foreach (var rule in rules)
            {
                var value = GetValue(features, rule.Feature);
                var present = rule.PositiveWhenGreater
                    ? value >= rule.Threshold
                    : value < rule.Threshold;

                confidence = Math.Min(confidence, EstimateConfidence(value, rule.Threshold));

                if (!present)
                    return new TextPresenceResult(false, confidence, nameof(FeatureThresholdTextPresenceDetector));
            }

            return new TextPresenceResult(
                true,
                confidence,
                nameof(FeatureThresholdTextPresenceDetector));
        }

        private static double GetValue(ImageFeatures features, ImageFeatureName feature)
        {
            return feature switch
            {
                ImageFeatureName.Mean => features.Mean,
                ImageFeatureName.StdDev => features.StdDev,
                ImageFeatureName.EdgeDensity => features.EdgeDensity,
                ImageFeatureName.BrightRatio => features.BrightRatio,
                ImageFeatureName.ActiveRowRatio => features.ActiveRowRatio,
                ImageFeatureName.LongestRowRunRatio => features.LongestRowRunRatio,
                ImageFeatureName.ActiveColumnRatio => features.ActiveColumnRatio,
                _ => 0
            };
        }

        private static double EstimateConfidence(double value, double threshold)
        {
            var denominator = Math.Max(Math.Abs(threshold), 0.000001);
            var distance = Math.Abs(value - threshold) / denominator;
            return Math.Clamp(0.5 + distance, 0, 1);
        }
    }
}
