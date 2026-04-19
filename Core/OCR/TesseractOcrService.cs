using OpenCvSharp;
using System;
using Tesseract;

namespace Aviscribe.Core.Ocr
{
    public class TesseractOcrService : IOcrService, IDisposable
    {
        private TesseractEngine _engine;
        private readonly string _languages;

        private int _callCount = 0;
        private readonly object _lock = new();

        public TesseractOcrService(string languages)
        {
            _languages = languages;

            _engine = CreateEngine(_languages);
        }

        private TesseractEngine CreateEngine(string languages)
        {
            var engine = new TesseractEngine(AppPaths.TessData, languages, EngineMode.Default);

            engine.SetVariable("debug_file", "/dev/null");

            // Reduce adaptive learning overhead (important for long runs)
            engine.SetVariable("load_system_dawg", "0");
            engine.SetVariable("load_freq_dawg", "0");
            engine.SetVariable("classify_enable_learning", "0");
            engine.SetVariable("classify_enable_adaptive_matcher", "0");

            return engine;
        }

        public string ReadText(Mat image)
        {
            if (image.Empty())
                return string.Empty;

            // Encode only ROI image (required for charlesw Tesseract)
            Cv2.ImEncode(".png", image, out var imageBytes);

            using var pix = Pix.LoadFromMemory(imageBytes);

            string text;

            lock (_lock)
            {
                using var page = _engine.Process(pix);

                text = page.GetText()?.Trim() ?? string.Empty;

                _callCount++;

                // periodic engine reset prevents native memory creep
                if (_callCount % 30 == 0)
                {
                    ResetEngine();
                }
            }

            return text;
        }

        private void ResetEngine()
        {
            _engine.Dispose();
            _engine = CreateEngine(_languages);
        }

        public void Dispose()
        {
            _engine?.Dispose();
        }
    }
}