using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public class LinearFeatureTextPresenceDetector : ITextPresenceDetector
    {
        private readonly Dictionary<OcrRegionType, LinearFeatureRegionModel> _regions;

        public LinearFeatureTextPresenceDetector(LinearFeatureModel model)
        {
            _regions = model.Regions.ToDictionary(region => region.RegionType);
        }

        public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
        {
            if (!_regions.TryGetValue(regionType, out var model))
                return TextPresenceResult.Absent(nameof(LinearFeatureTextPresenceDetector));

            var features = ImageFeatureExtractor.Extract(image);
            var probability = PredictProbability(model, features);

            return new TextPresenceResult(
                probability >= model.Threshold,
                probability,
                nameof(LinearFeatureTextPresenceDetector));
        }

        private static double PredictProbability(LinearFeatureRegionModel model, ImageFeatures features)
        {
            var score = model.Bias;

            for (var i = 0; i < model.Features.Count; i++)
            {
                var value = GetValue(features, model.Features[i]);
                var mean = i < model.Means.Count ? model.Means[i] : 0;
                var stdDev = i < model.StandardDeviations.Count ? model.StandardDeviations[i] : 1;
                var weight = i < model.Weights.Count ? model.Weights[i] : 0;

                score += weight * ((value - mean) / Math.Max(stdDev, 0.000001));
            }

            return 1.0 / (1.0 + Math.Exp(-Math.Clamp(score, -40, 40)));
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
    }
}
