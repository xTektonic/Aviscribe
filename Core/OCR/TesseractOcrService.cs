using OpenCvSharp;
using System;
using Tesseract;
using Aviscribe.Core.Capture;

namespace Aviscribe.Core.Ocr
{
    public class TesseractOcrService : IOcrService, IDisposable
    {
        private readonly TesseractEngine _engine;

        public TesseractOcrService(string languages)
        {
            _engine = new TesseractEngine(AppPaths.TessData, languages, EngineMode.Default);
        }

        public string ReadText(Mat image)
        {
            if (image.Empty())
                return string.Empty;

            // Encode Mat directly to PNG bytes (no System.Drawing)
            Cv2.ImEncode(".png", image, out var imageBytes);

            using var pix = Pix.LoadFromMemory(imageBytes);
            using var page = _engine.Process(pix);

            return page.GetText()?.Trim() ?? "";
        }

        public void Dispose()
        {
            _engine.Dispose();
        }
    }
}