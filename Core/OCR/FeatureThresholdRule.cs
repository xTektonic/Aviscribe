namespace Aviscribe.Core.Ocr
{
    public readonly record struct FeatureThresholdRule(
        OcrRegionType RegionType,
        ImageFeatureName Feature,
        double Threshold,
        bool PositiveWhenGreater);
}
