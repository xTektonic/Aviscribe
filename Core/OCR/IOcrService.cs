using Aviscribe.Core.Capture;
using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public interface IOcrService
    {
        string ReadText(Mat frame);
    }
}