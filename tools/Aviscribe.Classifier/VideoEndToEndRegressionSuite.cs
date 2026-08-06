using Aviscribe.Core;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class VideoEndToEndRegressionSuite
    {
        private static readonly Rect TalkatooDetectionBounds = new(600, 862, 715, 48);
        private static readonly Rect TalkatooOcrBounds = new(666, 862, 649, 48);
        private static readonly Rect MoonGetDetectionBounds = new(320, 600, 1250, 250);
        private static readonly Rect MoonGetOcrBounds = new(490, 797, 930, 60);
        private static readonly Rect StoryMoonBounds = new(450, 820, 1100, 150);

        private static readonly EndToEndExpectation[] Expectations =
        [
            new("cascade-talkatoo", OcrRegionType.Talkatoo, "Cascade", 18_318, 16),
            new("cascade-talkatoo-behind-waterfall", OcrRegionType.Talkatoo, "Cascade", 18_488, 4),
            new("cascade-talkatoo-waterfall-basin", OcrRegionType.Talkatoo, "Cascade", 18_656, 6),
            new("sand-talkatoo-top-dune", OcrRegionType.Talkatoo, "Sand", 24_704, 17),
            new("sand-talkatoo-skull-sign", OcrRegionType.Talkatoo, "Sand", 27_374, 55),
            new("lake-talkatoo-secret-room", OcrRegionType.Talkatoo, "Lake", 72_004, 17),
            new("lake-talkatoo-broken-pillar", OcrRegionType.Talkatoo, "Lake", 73_050, 7),
            new("wooded-talkatoo-elevator", OcrRegionType.Talkatoo, "Wooded", 82_562, 45),
            new("wooded-talkatoo-behind-rock-wall", OcrRegionType.Talkatoo, "Wooded", 117_404, 5),
            new("lost-talkatoo-caged-gold", OcrRegionType.Talkatoo, "Lost", 133_190, 3),
            new("seaside-talkatoo-valley", OcrRegionType.Talkatoo, "Seaside", 237_596, 45),
            new("luncheon-talkatoo-fork", OcrRegionType.Talkatoo, "Luncheon", 277_182, 41),

            new("cascade-storymoon-first", OcrRegionType.StoryMoon, "Cascade", 10_782, 1),
            new("sand-moonget-skull-sign", OcrRegionType.MoonGet, "Sand", 32_382, 55),
            new("sand-moonget-palm-notes", OcrRegionType.MoonGet, "Sand", 57_582, 32),
            new("lake-moonget-dorrie-rider", OcrRegionType.MoonGet, "Lake", 67_776, 2),
            new("lake-moonget-spiky-waterway", OcrRegionType.MoonGet, "Lake", 68_868, 8),
            new("lake-moonget-secret-room", OcrRegionType.MoonGet, "Lake", 72_632, 17),
            new("lake-moonget-broken-pillar", OcrRegionType.MoonGet, "Lake", 74_138, 7),
            new("wooded-moonget-fire-cave", OcrRegionType.MoonGet, "Wooded", 84_476, 19),
            new("wooded-moonget-stretching", OcrRegionType.MoonGet, "Wooded", 94_098, 25),
            new("seaside-moonget-northern", OcrRegionType.MoonGet, "Seaside", 233_982, 19),
        ];

        public static void Run(string videoPath, string outputDir, int windowFrames = 24, int stepFrames = 2)
        {
            Directory.CreateDirectory(outputDir);

            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            var detector = new HeuristicTextPresenceDetector();
            using var ocr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var repo = MoonRepository.LoadDefault();
            var settings = new RunSettings();
            var matcher = new MoonMatcher(repo, settings.InputLanguage);
            using var frame = new Mat();
            var failures = new List<EndToEndFailure>();

            foreach (var expectation in Expectations)
            {
                var detectedFrames = 0;
                var best = new MatchObservation(expectation.Frame, string.Empty, null, 0, false);
                var passed = false;

                for (var frameIndex = expectation.Frame - windowFrames;
                     frameIndex <= expectation.Frame + windowFrames;
                     frameIndex += Math.Max(1, stepFrames))
                {
                    if (frameIndex < 0)
                        continue;

                    capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
                    if (!capture.Read(frame) || frame.Empty())
                        continue;

                    var detectionBounds = DetectionBounds(expectation.RegionType);
                    using var detectionCrop = new Mat(frame, detectionBounds);
                    var presence = detector.Detect(expectation.RegionType, detectionCrop);
                    if (!presence.Present)
                        continue;

                    detectedFrames++;
                    using var ocrCrop = new Mat(frame, OcrBounds(expectation.RegionType));
                    var text = ocr.ReadText(ocrCrop);
                    var result = expectation.RegionType == OcrRegionType.Talkatoo
                        ? matcher.MatchTalkatooText(text, expectation.Kingdom, settings)
                        : matcher.MatchCollectionText(text, expectation.Kingdom, settings);

                    if (result.Score >= best.Score)
                    {
                        best = new MatchObservation(
                            frameIndex,
                            text,
                            result.BestMatch?.Id,
                            result.Score,
                            result.IsAmbiguous);
                    }

                    if (result.BestMatch?.Id != expectation.ExpectedMoonId || result.IsAmbiguous)
                        continue;

                    Console.WriteLine(
                        $"PASS {expectation.Name} at frame {frameIndex}: \"{text}\" -> {result.BestMatch.English}");
                    passed = true;
                    break;
                }

                if (passed)
                    continue;

                failures.Add(new EndToEndFailure(expectation, detectedFrames, best));
                WriteFailureImages(capture, frame, outputDir, expectation, best);
                Console.WriteLine(
                    $"FAIL {expectation.Name}: detections {detectedFrames}, " +
                    $"best frame {best.Frame}, moon {best.MoonId}, score {best.Score:0.000}, text \"{best.Text}\"");
            }

            if (failures.Count > 0)
                throw new InvalidOperationException($"{failures.Count} end-to-end video expectation(s) failed.");

            Console.WriteLine($"Video end-to-end regression passed: {Expectations.Length} expected event windows resolved.");
        }

        private static Rect DetectionBounds(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.Talkatoo => TalkatooDetectionBounds,
                OcrRegionType.MoonGet => MoonGetDetectionBounds,
                OcrRegionType.StoryMoon => StoryMoonBounds,
                _ => throw new ArgumentOutOfRangeException(nameof(regionType))
            };
        }

        private static Rect OcrBounds(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.Talkatoo => TalkatooOcrBounds,
                OcrRegionType.MoonGet => MoonGetOcrBounds,
                OcrRegionType.StoryMoon => StoryMoonBounds,
                _ => throw new ArgumentOutOfRangeException(nameof(regionType))
            };
        }

        private static void WriteFailureImages(
            VideoCapture capture,
            Mat frame,
            string outputDir,
            EndToEndExpectation expectation,
            MatchObservation best)
        {
            var frameIndex = best.Text.Length > 0 ? best.Frame : expectation.Frame;
            capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
            if (!capture.Read(frame) || frame.Empty())
                return;

            var prefix = $"{expectation.Name}_frame_{frameIndex:D7}";
            using var detectionCrop = new Mat(frame, DetectionBounds(expectation.RegionType));
            using var ocrCrop = new Mat(frame, OcrBounds(expectation.RegionType));
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_full.jpg"), frame);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_detection.jpg"), detectionCrop);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_ocr.jpg"), ocrCrop);
        }

        private readonly record struct EndToEndExpectation(
            string Name,
            OcrRegionType RegionType,
            string Kingdom,
            int Frame,
            int ExpectedMoonId);

        private readonly record struct MatchObservation(
            int Frame,
            string Text,
            int? MoonId,
            double Score,
            bool IsAmbiguous);

        private readonly record struct EndToEndFailure(
            EndToEndExpectation Expectation,
            int DetectedFrames,
            MatchObservation Best);
    }
}
