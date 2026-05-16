using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public enum OcrRegionType
    {
        Talkatoo,
        MoonGet
    }

    public record OcrRegion(
        OcrRegionType Type,
        Rect Bounds,
        Func<Mat, bool> Detection
    );
}