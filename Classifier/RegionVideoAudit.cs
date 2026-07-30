using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class RegionVideoAudit
    {
        public static void Run(
            CollectionConfirmationProfile profile,
            string videoPath,
            string outputDir,
            int stride,
            int maxSaved,
            int maxFrames,
            int startFrame = 0)
        {
            if (stride <= 0)
                throw new ArgumentOutOfRangeException(nameof(stride));

            Directory.CreateDirectory(outputDir);

            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            if (startFrame > 0)
                capture.Set(VideoCaptureProperties.PosFrames, startFrame);

            var detector = new HeuristicTextPresenceDetector();
            var tracker = new CollectionConfirmationTracker(profile);
            using var frame = new Mat();
            using var transitions = new StreamWriter(
                Path.Combine(outputDir, "transitions.csv"));
            transitions.WriteLine(
                "event,source_frame,generation,present,confirmed," +
                "consecutive_present,consecutive_absent,present_observations");

            var sourceFrameIndex = Math.Max(0, startFrame);
            var processedFrameCount = 0L;
            var inspectedObservations = 0;
            var positiveObservations = 0;
            var savedCount = 0;
            var confirmedAppearances = 0;
            var confirmedReleases = 0;
            var negativeRun = 0;
            var longestBoundedNegativeRun = 0;
            int? lastPositiveFrame = null;

            while (capture.Read(frame) && !frame.Empty())
            {
                if (maxFrames > 0 &&
                    sourceFrameIndex >= startFrame + maxFrames)
                {
                    break;
                }

                if ((sourceFrameIndex - startFrame) % stride != 0)
                {
                    sourceFrameIndex++;
                    continue;
                }

                processedFrameCount++;
                if (!tracker.ShouldInspect(processedFrameCount))
                {
                    sourceFrameIndex++;
                    continue;
                }

                inspectedObservations++;
                using var crop = new Mat(frame, profile.DetectionBounds);
                var result = detector.Detect(profile.RegionType, crop);
                var before = tracker.Snapshot();
                var after = tracker.Observe(result.Present);

                if (result.Present)
                {
                    positiveObservations++;
                    if (lastPositiveFrame != null && negativeRun > 0)
                    {
                        longestBoundedNegativeRun = Math.Max(
                            longestBoundedNegativeRun,
                            negativeRun);
                        transitions.WriteLine(
                            $"bounded_absence,{sourceFrameIndex},{after.Generation}," +
                            $"true,{after.Confirmed},{after.ConsecutivePresent}," +
                            $"{negativeRun},{after.PresentObservationCount}");
                    }

                    lastPositiveFrame = sourceFrameIndex;
                    negativeRun = 0;
                }
                else if (lastPositiveFrame != null)
                {
                    negativeRun++;
                }

                var becameConfirmed =
                    after.Active &&
                    after.CurrentlyPresent &&
                    after.Confirmed &&
                    (!before.Confirmed ||
                     before.Generation != after.Generation);
                if (becameConfirmed)
                {
                    tracker.RecordEnqueued(after.Generation, attempt: 1);
                    tracker.RecordOutcome(after.Generation, resolved: true);
                    after = tracker.Snapshot();
                    confirmedAppearances++;
                    transitions.WriteLine(
                        $"confirmed,{sourceFrameIndex},{after.Generation}," +
                        $"true,true,{after.ConsecutivePresent}," +
                        $"{after.ConsecutiveAbsent},{after.PresentObservationCount}");
                    if (savedCount < maxSaved)
                    {
                        var prefix =
                            $"confirmed_{sourceFrameIndex:D7}_" +
                            $"generation_{after.Generation:D4}_" +
                            $"conf_{result.Confidence:0.000}";
                        Cv2.ImWrite(
                            Path.Combine(outputDir, $"{prefix}_crop.jpg"),
                            crop);
                        Cv2.ImWrite(
                            Path.Combine(outputDir, $"{prefix}_frame.jpg"),
                            frame);
                        savedCount++;
                    }
                }

                if (before.Active && !after.Active)
                {
                    confirmedReleases++;
                    transitions.WriteLine(
                        $"released,{sourceFrameIndex},{before.Generation}," +
                        $"false,{before.Confirmed},0," +
                        $"{profile.RequiredAbsentObservations}," +
                        $"{before.PresentObservationCount}");
                }

                sourceFrameIndex++;
            }

            Console.WriteLine(
                $"{profile.RegionType} production-parity audit scanned source frames " +
                $"{startFrame}..{sourceFrameIndex}: inspected {inspectedObservations} " +
                $"observations every {profile.DetectionIntervalFrames} processed frame(s), " +
                $"positive observations {positiveObservations}, confirmed appearances " +
                $"{confirmedAppearances}, releases {confirmedReleases}, longest bounded " +
                $"detector gap {longestBoundedNegativeRun} inspected absence observation(s), " +
                $"release threshold {profile.RequiredAbsentObservations}, saved {savedCount} " +
                $"samples and transitions.csv to {outputDir}.");
        }
    }
}
