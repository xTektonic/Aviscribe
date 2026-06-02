using Aviscribe.Core;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class AllMoonsVideoRegressionSuite
    {
        private static readonly Rect TalkatooDetectionBounds = new(666, 862, 649, 48);
        private static readonly Rect TalkatooOcrBounds = new(666, 862, 649, 48);
        private static readonly Rect MoonGetDetectionBounds = new(320, 600, 1250, 250);
        private static readonly Rect MoonGetOcrBounds = new(490, 797, 930, 60);
        private static readonly Rect StoryMoonBounds = new(450, 820, 1100, 150);

        private static readonly AllMoonsExpectation[] Expectations =
        [
            new("sand-talkatoo-employees-only", OcrRegionType.Talkatoo, "Sand", 218_490, 43),
            new("sand-talkatoo-alcove-ruins", OcrRegionType.Talkatoo, "Sand", 219_830, 6),
            new("sand-talkatoo-bird-wastes", OcrRegionType.Talkatoo, "Sand", 224_020, 22),
            new("sand-talkatoo-flowing-sands", OcrRegionType.Talkatoo, "Sand", 224_178, 8),
            new("sand-talkatoo-rumble-floor", OcrRegionType.Talkatoo, "Sand", 228_358, 52),
            new("sand-talkatoo-captain-toad", OcrRegionType.Talkatoo, "Sand", 228_518, 37),
            new("mushroom-talkatoo-mushroom-art", OcrRegionType.Talkatoo, "Mushroom", 1_299_062, 41),
            new("mushroom-talkatoo-peach-castle-love", OcrRegionType.Talkatoo, "Mushroom", 1_299_224, 16),
            new("metro-talkatoo-jump-rope-genius", OcrRegionType.Talkatoo, "Metro", 652_332, 30),
            new("metro-talkatoo-building-planter", OcrRegionType.Talkatoo, "Metro", 661_446, 21),
            new("metro-talkatoo-sewer-treasure", OcrRegionType.Talkatoo, "Metro", 661_602, 35),
            new("metro-talkatoo-tourist", OcrRegionType.Talkatoo, "Metro", 676_726, 52),
            new("metro-talkatoo-celebrating-streets", OcrRegionType.Talkatoo, "Metro", 681_594, 36),
            new("luncheon-talkatoo-two-flames", OcrRegionType.Talkatoo, "Luncheon", 1_090_804, 31),
            new("luncheon-talkatoo-captain-toad", OcrRegionType.Talkatoo, "Luncheon", 1_099_184, 33),
            new("luncheon-talkatoo-kingdom-art", OcrRegionType.Talkatoo, "Luncheon", 1_099_358, 49),
            new("luncheon-talkatoo-big-pot-swim", OcrRegionType.Talkatoo, "Luncheon", 1_099_530, 36),
            new("luncheon-talkatoo-volcano-hop", OcrRegionType.Talkatoo, "Luncheon", 1_106_554, 35),
            new("luncheon-talkatoo-veggies-chest", OcrRegionType.Talkatoo, "Luncheon", 1_107_868, 34),
            new("luncheon-talkatoo-tourist", OcrRegionType.Talkatoo, "Luncheon", 1_114_498, 48),

            new("cascade-story-first-power-moon", OcrRegionType.StoryMoon, "Cascade", 16_317, 1),
            new("cascade-story-multi-moon-atop-falls", OcrRegionType.StoryMoon, "Cascade", 22_212, 2),
            new("sand-moonget-lone-pillar", OcrRegionType.MoonGet, "Sand", 68_108, 13),
            new("sand-moonget-alcove-ruins", OcrRegionType.MoonGet, "Sand", 221_060, 6),
            new("sand-moonget-bird-wastes", OcrRegionType.MoonGet, "Sand", 225_775, 22),
            new("sand-moonget-flowing-sands", OcrRegionType.MoonGet, "Sand", 227_330, 8),
            new("mushroom-moonget-rescue-peach", OcrRegionType.MoonGet, "Mushroom", 1_296_365, 44),
            new("mushroom-moonget-loose-tile", OcrRegionType.MoonGet, "Mushroom", 1_297_180, 26),
            new("metro-moonget-rooftop-hop", OcrRegionType.MoonGet, "Metro", 649_105, 25),
            new("metro-moonget-hidden-scrap", OcrRegionType.MoonGet, "Metro", 650_100, 15),
            new("luncheon-moonget-volcano-hop", OcrRegionType.MoonGet, "Luncheon", 1_107_280, 35),
            new("luncheon-moonget-veggies-chest", OcrRegionType.MoonGet, "Luncheon", 1_111_510, 34),

            new("sand-story-atop-highest-tower", OcrRegionType.StoryMoon, "Sand", 76_770, 1),
            new("sand-story-inverted-pyramid", OcrRegionType.StoryMoon, "Sand", 125_550, 3),
        ];

        private static readonly NegativeExpectation[] NegativeExpectations =
        [
            new("sand-platform-after-lone-pillar-not-moonget", OcrRegionType.MoonGet, 68_624),
            new("sand-yellow-platform-not-talkatoo", OcrRegionType.Talkatoo, 217_178),
        ];

        public static void Run(string videoPath, string outputDir, int windowFrames = 30, int stepFrames = 2)
        {
            Directory.CreateDirectory(outputDir);

            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            var detector = new HeuristicTextPresenceDetector();
            using var ocr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var repo = MoonRepository.LoadDefault();
            var settings = new RunSettings
            {
                IncludePostGameKingdoms = true
            };
            var matcher = new MoonMatcher(repo, settings.InputLanguage, settings.OutputLanguage);
            using var frame = new Mat();
            var failures = new List<AllMoonsFailure>();

            foreach (var expectation in Expectations.OrderBy(expectation => expectation.Frame))
            {
                var detectedFrames = 0;
                var best = new MatchObservation(expectation.Frame, string.Empty, null, 0, false);
                var passed = false;
                var startFrame = Math.Max(0, expectation.Frame - windowFrames);
                var endFrame = expectation.Frame + windowFrames;
                capture.Set(VideoCaptureProperties.PosFrames, startFrame);

                for (var frameIndex = startFrame; frameIndex <= endFrame; frameIndex++)
                {
                    if (!capture.Read(frame) || frame.Empty())
                        break;

                    if ((frameIndex - startFrame) % Math.Max(1, stepFrames) != 0)
                        continue;

                    using var detectionCrop = new Mat(frame, DetectionBounds(expectation.RegionType));
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

                failures.Add(new AllMoonsFailure(expectation, detectedFrames, best));
                WriteFailureImages(capture, frame, outputDir, expectation, best);
                Console.WriteLine(
                    $"FAIL {expectation.Name}: detections {detectedFrames}, " +
                    $"best frame {best.Frame}, moon {best.MoonId}, score {best.Score:0.000}, text \"{best.Text}\"");
            }

            foreach (var expectation in NegativeExpectations)
            {
                capture.Set(VideoCaptureProperties.PosFrames, expectation.Frame);
                if (!capture.Read(frame) || frame.Empty())
                    continue;

                using var detectionCrop = new Mat(frame, DetectionBounds(expectation.RegionType));
                var presence = detector.Detect(expectation.RegionType, detectionCrop);
                if (!presence.Present)
                {
                    Console.WriteLine($"PASS {expectation.Name} absent at frame {expectation.Frame}");
                    continue;
                }

                failures.Add(new AllMoonsFailure(
                    new AllMoonsExpectation(expectation.Name, expectation.RegionType, string.Empty, expectation.Frame, 0),
                    1,
                    new MatchObservation(expectation.Frame, string.Empty, null, presence.Confidence, false)));
                WriteNegativeFailureImages(frame, outputDir, expectation);
                Console.WriteLine(
                    $"FAIL {expectation.Name}: false positive at frame {expectation.Frame}, confidence {presence.Confidence:0.000}");
            }

            if (failures.Count > 0)
                throw new InvalidOperationException($"{failures.Count} all-moons video expectation(s) failed.");

            Console.WriteLine(
                $"All-moons video regression passed: {Expectations.Length} expected windows resolved, " +
                $"{NegativeExpectations.Length} negative windows stayed quiet.");
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
            AllMoonsExpectation expectation,
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

        private static void WriteNegativeFailureImages(
            Mat frame,
            string outputDir,
            NegativeExpectation expectation)
        {
            var prefix = $"{expectation.Name}_frame_{expectation.Frame:D7}";
            using var detectionCrop = new Mat(frame, DetectionBounds(expectation.RegionType));
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_full.jpg"), frame);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_detection.jpg"), detectionCrop);
        }

        private readonly record struct AllMoonsExpectation(
            string Name,
            OcrRegionType RegionType,
            string Kingdom,
            int Frame,
            int ExpectedMoonId);

        private readonly record struct NegativeExpectation(
            string Name,
            OcrRegionType RegionType,
            int Frame);

        private readonly record struct MatchObservation(
            int Frame,
            string Text,
            int? MoonId,
            double Score,
            bool IsAmbiguous);

        private readonly record struct AllMoonsFailure(
            AllMoonsExpectation Expectation,
            int DetectedFrames,
            MatchObservation Best);
    }
}
