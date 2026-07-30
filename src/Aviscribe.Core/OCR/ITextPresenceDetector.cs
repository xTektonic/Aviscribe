using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public interface ITextPresenceDetector
    {
        TextPresenceResult Detect(OcrRegionType regionType, Mat image);
    }
}
