using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class RegionVideoAudit
    {
        public static void Run(
            OcrRegionType regionType,
            Rect bounds,
            int stableFrameCount,
            int stableImageMaxHammingDistance,
            string videoPath,
            string outputDir,
            int stride,
            int maxSaved,
            int maxFrames)
        {
            Directory.CreateDirectory(outputDir);

            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            var detector = new HeuristicTextPresenceDetector();
            using var frame = new Mat();
            var frameIndex = 0;
            var positiveCount = 0;
            var savedCount = 0;
            var previousPositive = false;
            var positiveRun = 0;
            var longestPositiveRun = 0;
            var stableWindows = 0;
            var stableRuns = 0;
            var previousStable = false;
            var detectionWindow = new Queue<bool>();
            var hashWindow = new Queue<ulong>();

            while (capture.Read(frame) && !frame.Empty())
            {
                frameIndex++;
                if (maxFrames > 0 && frameIndex > maxFrames)
                    break;

                if (frameIndex % stride != 0)
                    continue;

                using var crop = new Mat(frame, bounds);
                var result = detector.Detect(regionType, crop);

                if (result.Present)
                    positiveCount++;

                positiveRun = result.Present ? positiveRun + 1 : 0;
                longestPositiveRun = Math.Max(longestPositiveRun, positiveRun);

                detectionWindow.Enqueue(result.Present);
                hashWindow.Enqueue(result.Present ? ImageHash.Compute(crop) : 0);
                if (detectionWindow.Count > stableFrameCount)
                {
                    detectionWindow.Dequeue();
                    hashWindow.Dequeue();
                }

                var stable = detectionWindow.Count == stableFrameCount &&
                             detectionWindow.All(x => x) &&
                             IsStableHashWindow(hashWindow, stableImageMaxHammingDistance);

                if (stable)
                {
                    stableWindows++;
                    if (!previousStable)
                    {
                        stableRuns++;
                        if (savedCount < maxSaved)
                        {
                            var prefix = $"stable_{frameIndex:D7}_conf_{result.Confidence:0.000}";
                            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_crop.jpg"), crop);
                            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_frame.jpg"), frame);
                            savedCount++;
                        }
                    }
                }

                if (result.Present && (!previousPositive || savedCount < 10) && savedCount < maxSaved)
                {
                    var prefix = $"frame_{frameIndex:D7}_conf_{result.Confidence:0.000}";
                    Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_crop.jpg"), crop);
                    Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_frame.jpg"), frame);
                    savedCount++;
                }

                previousStable = stable;
                previousPositive = result.Present;
            }

            Console.WriteLine(
                $"Scanned {frameIndex} frames, positives {positiveCount}, longest run {longestPositiveRun}, " +
                $"stable windows {stableWindows}, stable runs {stableRuns}, saved {savedCount} samples to {outputDir}");
        }

        private static bool IsStableHashWindow(IEnumerable<ulong> hashes, int maxHammingDistance)
        {
            using var enumerator = hashes.GetEnumerator();
            if (!enumerator.MoveNext())
                return false;

            var first = enumerator.Current;
            do
            {
                if (ImageHash.Hamming(first, enumerator.Current) > maxHammingDistance)
                    return false;
            }
            while (enumerator.MoveNext());

            return true;
        }
    }
}
