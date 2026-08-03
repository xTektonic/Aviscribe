using OpenCvSharp;

namespace Aviscribe.Core.KingdomDetection;

public interface IKingdomDetector
{
    KingdomDetectionResult Detect(Mat referenceFrame);
}
