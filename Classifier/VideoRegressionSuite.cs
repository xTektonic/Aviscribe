using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class VideoRegressionSuite
    {
        private static readonly Rect TalkatooBounds = new(666, 862, 649, 48);
        private static readonly Rect MoonGetDetectionBounds = new(320, 600, 1250, 250);

        private static readonly VideoExpectation[] Expectations =
        [
            new("cascade-talkatoo-005m", OcrRegionType.Talkatoo, 18_318, TalkatooBounds),
            new("cascade-talkatoo-behind-waterfall-005m", OcrRegionType.Talkatoo, 18_488, TalkatooBounds),
            new("cascade-talkatoo-waterfall-basin-005m", OcrRegionType.Talkatoo, 18_656, TalkatooBounds),
            new("sand-talkatoo-006m", OcrRegionType.Talkatoo, 24_704, TalkatooBounds),
            new("sand-talkatoo-007m", OcrRegionType.Talkatoo, 27_376, TalkatooBounds),
            new("lake-talkatoo-secret-room-020m", OcrRegionType.Talkatoo, 72_004, TalkatooBounds),
            new("lake-talkatoo-broken-pillar-020m", OcrRegionType.Talkatoo, 73_050, TalkatooBounds),
            new("wooded-talkatoo-elevator-022m", OcrRegionType.Talkatoo, 82_562, TalkatooBounds),
            new("wooded-talkatoo-032m", OcrRegionType.Talkatoo, 117_420, TalkatooBounds),
            new("lost-talkatoo-037m", OcrRegionType.Talkatoo, 133_200, TalkatooBounds),
            new("seaside-talkatoo-066m", OcrRegionType.Talkatoo, 237_600, TalkatooBounds),
            new("luncheon-talkatoo-077m", OcrRegionType.Talkatoo, 277_200, TalkatooBounds),

            new("cap-moonget-003m", OcrRegionType.MoonGet, 10_800, MoonGetDetectionBounds),
            new("sand-moonget-009m", OcrRegionType.MoonGet, 32_400, MoonGetDetectionBounds),
            new("sand-moonget-012m", OcrRegionType.MoonGet, 43_200, MoonGetDetectionBounds),
            new("sand-moonget-015m", OcrRegionType.MoonGet, 54_000, MoonGetDetectionBounds),
            new("lake-moonget-016m", OcrRegionType.MoonGet, 57_600, MoonGetDetectionBounds),
            new("lake-moonget-broken-pillar-020m", OcrRegionType.MoonGet, 74_138, MoonGetDetectionBounds),
            new("wooded-moonget-fire-cave-023m", OcrRegionType.MoonGet, 84_476, MoonGetDetectionBounds),
            new("wooded-moonget-stretching-026m", OcrRegionType.MoonGet, 94_098, MoonGetDetectionBounds),
            new("wooded-moonget-032m", OcrRegionType.MoonGet, 115_200, MoonGetDetectionBounds),
            new("lost-moonget-040m", OcrRegionType.MoonGet, 144_000, MoonGetDetectionBounds),
            new("metro-moonget-053m", OcrRegionType.MoonGet, 190_800, MoonGetDetectionBounds),
            new("snow-moonget-058m", OcrRegionType.MoonGet, 208_800, MoonGetDetectionBounds),
            new("seaside-moonget-065m", OcrRegionType.MoonGet, 234_000, MoonGetDetectionBounds),
        ];

        private static readonly VideoExpectation[] NegativeExpectations =
        [
            new("cascade-background-after-fast-talkatoo", OcrRegionType.Talkatoo, 18_989, TalkatooBounds),
        ];

        public static void Run(string videoPath, string outputDir, int windowFrames = 18, int stepFrames = 2)
        {
            Directory.CreateDirectory(outputDir);

            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            var detector = new HeuristicTextPresenceDetector();
            using var frame = new Mat();
            var failures = new List<VideoExpectation>();

            foreach (var expectation in Expectations)
            {
                var detected = false;
                TextPresenceResult bestResult = TextPresenceResult.Absent(nameof(VideoRegressionSuite));
                var bestFrame = expectation.Frame;

                for (var frameIndex = expectation.Frame - windowFrames;
                     frameIndex <= expectation.Frame + windowFrames;
                     frameIndex += Math.Max(1, stepFrames))
                {
                    if (frameIndex < 0)
                        continue;

                    capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
                    if (!capture.Read(frame) || frame.Empty())
                        continue;

                    using var crop = new Mat(frame, expectation.Bounds);
                    var result = detector.Detect(expectation.RegionType, crop);
                    if (result.Confidence >= bestResult.Confidence)
                    {
                        bestResult = result;
                        bestFrame = frameIndex;
                    }

                    if (!result.Present)
                        continue;

                    detected = true;
                    bestResult = result;
                    bestFrame = frameIndex;
                    break;
                }

                if (detected)
                {
                    Console.WriteLine($"PASS {expectation.Name} at frame {bestFrame}");
                    continue;
                }

                failures.Add(expectation);
                WriteFailureImages(capture, frame, outputDir, expectation);
                Console.WriteLine($"FAIL {expectation.Name} near frame {expectation.Frame} (best {bestResult.Confidence:0.000})");
            }

            foreach (var expectation in NegativeExpectations)
            {
                capture.Set(VideoCaptureProperties.PosFrames, expectation.Frame);
                if (!capture.Read(frame) || frame.Empty())
                    continue;

                using var crop = new Mat(frame, expectation.Bounds);
                var result = detector.Detect(expectation.RegionType, crop);
                if (!result.Present)
                {
                    Console.WriteLine($"PASS {expectation.Name} absent at frame {expectation.Frame}");
                    continue;
                }

                failures.Add(expectation);
                WriteFailureImages(capture, frame, outputDir, expectation);
                Console.WriteLine($"FAIL {expectation.Name} false positive at frame {expectation.Frame} (confidence {result.Confidence:0.000})");
            }

            if (failures.Count > 0)
                throw new InvalidOperationException($"{failures.Count} video regression expectation(s) missed.");

            Console.WriteLine(
                $"Video regression passed: {Expectations.Length} expected event windows detected, " +
                $"{NegativeExpectations.Length} negative windows stayed quiet.");
        }

        private static void WriteFailureImages(
            VideoCapture capture,
            Mat frame,
            string outputDir,
            VideoExpectation expectation)
        {
            capture.Set(VideoCaptureProperties.PosFrames, expectation.Frame);
            if (!capture.Read(frame) || frame.Empty())
                return;

            using var crop = new Mat(frame, expectation.Bounds);
            var prefix = $"{expectation.Name}_frame_{expectation.Frame:D7}";
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_full.jpg"), frame);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_crop.jpg"), crop);
        }

        private readonly record struct VideoExpectation(
            string Name,
            OcrRegionType RegionType,
            int Frame,
            Rect Bounds);
    }
}
