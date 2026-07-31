namespace Aviscribe.Core.Ocr
{
    public readonly record struct ImageFeatures(
        int Width,
        int Height,
        double Mean,
        double StdDev,
        double EdgeDensity,
        double BrightRatio,
        double ActiveRowRatio,
        double LongestRowRunRatio,
        double ActiveColumnRatio);
}
