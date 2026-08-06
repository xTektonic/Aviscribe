using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class CaptureCropSmoke
    {
        public static void Run()
        {
            DefaultCropPreservesReferenceFrame();
            CropScalesWithSourceResolution();
            InvalidCropFallsBackSafely();
            FrameProcessorSeesOnlyNormalizedGameplay();
            Console.WriteLine("Capture crop smoke passed.");
        }

        private static void DefaultCropPreservesReferenceFrame()
        {
            var crop = CaptureCropSettings.Default.Resolve(1920, 1080);
            Expect(crop == new Rect(0, 0, 1920, 1080), "Default crop should preserve 1920x1080.");

            var smaller = CaptureCropSettings.Default.Resolve(1280, 720);
            Expect(smaller == new Rect(0, 0, 1280, 720), "Default crop should scale to a 720p source.");
        }

        private static void CropScalesWithSourceResolution()
        {
            var settings = CaptureCropSettings.FromRect(
                1920,
                1080,
                new Rect(160, 90, 1600, 900));
            var resolved = settings.Resolve(1280, 720);

            Expect(resolved.X >= 0 && resolved.Y >= 0, "Scaled crop should stay within the source.");
            Expect(
                resolved.Right <= 1280 && resolved.Bottom <= 720,
                "Scaled crop should not exceed the source.");
            Expect(
                resolved.Width * CaptureCropSettings.AspectHeight ==
                    resolved.Height * CaptureCropSettings.AspectWidth,
                "Scaled crop should remain 16:9.");
        }

        private static void InvalidCropFallsBackSafely()
        {
            var invalid = new CaptureCropSettings
            {
                SourceWidth = 1920,
                SourceHeight = 1080,
                X = -10,
                Y = 0,
                Width = 0,
                Height = 0
            };

            var resolved = invalid.Resolve(1600, 1200);
            Expect(resolved == new Rect(0, 150, 1600, 900), "Invalid crop should use the largest centered 16:9 area.");
        }

        private static void FrameProcessorSeesOnlyNormalizedGameplay()
        {
            var repo = MoonRepository.LoadDefault();
            var state = new GameState();
            state.SetKingdom("Sand");
            var matcher = new MoonMatcher(
                repo,
                state.Settings.InputLanguage);
            using var detector = new GameplayProbeDetector();
            using var ocr = new NoOpOcrService();
            var crop = CaptureCropSettings.FromRect(
                2080,
                1170,
                new Rect(80, 45, 1920, 1080));
            var processor = new FrameProcessor(ocr, matcher, state, detector, crop);

            using var source = new Mat(new Size(2080, 1170), MatType.CV_8UC3, new Scalar(240, 10, 10));
            using (var gameplay = new Mat(source, crop.Resolve(source.Width, source.Height)))
                gameplay.SetTo(Scalar.Black);

            var talkatooDetection = OcrReferenceLayout.Talkatoo.DetectionBounds;
            source.Set(
                crop.Y + talkatooDetection.Y,
                crop.X + talkatooDetection.X,
                new Vec3b(17, 29, 43));

            processor.Start();
            try
            {
                processor.PushFrame(new VideoFrame(source.Clone(), DateTime.UtcNow));
                Expect(
                    detector.Inspected.Wait(TimeSpan.FromSeconds(3)),
                    "FrameProcessor did not inspect the normalized gameplay frame.");
                Expect(detector.SawExpectedCrop, "Detector did not receive the expected cropped gameplay pixels.");
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }

        private sealed class GameplayProbeDetector : ITextPresenceDetector, IDisposable
        {
            public ManualResetEventSlim Inspected { get; } = new(false);
            public bool SawExpectedCrop { get; private set; }

            public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
            {
                if (regionType == OcrRegionType.Talkatoo)
                {
                    SawExpectedCrop =
                        image.Width == OcrReferenceLayout.Talkatoo.DetectionBounds.Width &&
                        image.Height == OcrReferenceLayout.Talkatoo.DetectionBounds.Height &&
                        image.At<Vec3b>(0, 0) == new Vec3b(17, 29, 43);
                    Inspected.Set();
                }

                return TextPresenceResult.Absent(nameof(GameplayProbeDetector));
            }

            public void Dispose()
            {
                Inspected.Dispose();
            }
        }

        private sealed class NoOpOcrService : IOcrService, IDisposable
        {
            public string ReadText(Mat frame) => string.Empty;

            public void Dispose()
            {
            }
        }
    }
}
