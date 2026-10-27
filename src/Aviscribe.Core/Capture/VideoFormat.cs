namespace Aviscribe.Core.Capture;

public sealed record VideoFormat(
    int Width,
    int Height,
    string PixelFormat,
    int FrameRateNumerator,
    int FrameRateDenominator,
    string Description = "")
{
    public string? SourceFormatId { get; init; }
    public double FramesPerSecond =>
        FrameRateDenominator == 0
            ? 0
            : FrameRateNumerator / (double)FrameRateDenominator;

    public string Id =>
        SourceFormatId ?? $"{Width}x{Height}:{PixelFormat}:{FrameRateNumerator}/{FrameRateDenominator}";

    public override string ToString()
    {
        var rate = FramesPerSecond;
        var rateText = rate > 0 ? $"{rate:0.##} fps" : "unknown fps";
        return $"{Width} × {Height} {PixelFormat} ({rateText})";
    }
}
