using Aviscribe.Core;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class SuspiciousVideoEventAudit
    {
        private static readonly Rect TalkatooBounds = new(666, 862, 649, 48);
        private static readonly Rect MoonGetDetectionBounds = new(320, 600, 1250, 250);
        private static readonly Rect MoonGetOcrBounds = new(490, 797, 930, 60);
        private static readonly Rect StoryMoonBounds = new(450, 820, 1100, 150);

        public static void Run(
            string videoPath,
            string outputDir,
            OcrRegionType regionType,
            int startFrame,
            int maxFrames,
            int stride,
            int maxSaved,
            double minimumScore,
            string? kingdom)
        {
            if (stride <= 0)
                throw new ArgumentOutOfRangeException(nameof(stride), "Stride must be positive.");

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
            var matcher = new MoonMatcher(repo, settings.InputLanguage);
            var candidates = Candidates(repo, regionType, settings, kingdom);
            var detectionBounds = DetectionBounds(regionType);
            var ocrBounds = OcrBounds(regionType);
            var stability = Stability(regionType);
            using var frame = new Mat();
            using var writer = new StreamWriter(Path.Combine(outputDir, "suspicious-events.csv"));
            writer.AutoFlush = true;

            var detectionWindow = new Queue<bool>();
            var hashWindow = new Queue<ulong>();
            var previousStable = false;
            var stableRuns = 0;
            var suspiciousRuns = 0;
            var suppressedRuns = 0;
            var saved = 0;
            var endFrame = maxFrames <= 0 ? int.MaxValue : startFrame + maxFrames;

            writer.WriteLine("frame,region,kingdom,ocr_text,best_id,best_kingdom,best_english,score,ambiguous,reason");
            capture.Set(VideoCaptureProperties.PosFrames, Math.Max(0, startFrame));

            for (var frameIndex = Math.Max(0, startFrame);
                 frameIndex <= endFrame && saved < maxSaved;
                 frameIndex++)
            {
                if (!capture.Grab())
                    break;

                if ((frameIndex - startFrame) % stride != 0)
                    continue;

                if (!capture.Retrieve(frame) || frame.Empty())
                    break;

                using var detectionCrop = new Mat(frame, detectionBounds);
                var presence = detector.Detect(regionType, detectionCrop);
                detectionWindow.Enqueue(presence.Present);
                hashWindow.Enqueue(presence.Present ? ImageHash.Compute(detectionCrop) : 0);

                if (detectionWindow.Count > stability.StableFrameCount)
                {
                    detectionWindow.Dequeue();
                    hashWindow.Dequeue();
                }

                var stable = detectionWindow.Count == stability.StableFrameCount &&
                    detectionWindow.All(present => present) &&
                    IsStableHashWindow(hashWindow, stability.StableImageMaxHammingDistance);

                if (!stable)
                {
                    previousStable = false;
                    continue;
                }

                if (previousStable)
                    continue;

                stableRuns++;

                using var ocrCrop = new Mat(frame, ocrBounds);
                var observation = Observe(ocr, matcher, candidates, ocrCrop, frameIndex);
                var reason = SuspiciousReason(observation.Match, minimumScore);
                if (reason != null)
                {
                    observation = BestNearbyObservation(
                        capture,
                        detector,
                        ocr,
                        matcher,
                        candidates,
                        regionType,
                        detectionBounds,
                        ocrBounds,
                        frameIndex,
                        searchBackFrames: SearchBackFrames(regionType),
                        searchForwardFrames: SearchForwardFrames(regionType),
                        stride);
                    reason = SuspiciousReason(observation.Match, minimumScore);
                }

                if (reason == null)
                {
                    RestoreCapturePosition(capture, frameIndex);
                    previousStable = true;
                    continue;
                }

                if (IsSuppressedByCollectionScreen(capture, detector, regionType, observation.Frame))
                {
                    suppressedRuns++;
                    RestoreCapturePosition(capture, frameIndex);
                    previousStable = true;
                    continue;
                }

                suspiciousRuns++;
                using var eventFrame = ReadFrame(capture, observation.Frame);
                using var eventDetectionCrop = new Mat(eventFrame, detectionBounds);
                using var eventOcrCrop = new Mat(eventFrame, ocrBounds);
                WriteEvent(outputDir, eventFrame, eventDetectionCrop, eventOcrCrop, observation.Frame, regionType, presence, observation.Text, observation.Match, reason);
                writer.WriteLine(
                    $"{observation.Frame},{regionType},{kingdom ?? "*"}," +
                    $"{Csv(observation.Text)},{observation.Match.BestMatch?.Id},{observation.Match.BestMatch?.Kingdom}," +
                    $"{Csv(observation.Match.BestMatch?.English ?? string.Empty)},{observation.Match.Score:0.000},{observation.Match.IsAmbiguous},{reason}");
                Console.WriteLine(
                    $"{observation.Frame:D7} suspicious {regionType}: \"{observation.Text}\" -> " +
                    $"{observation.Match.BestMatch?.Id} {observation.Match.BestMatch?.Kingdom} {observation.Match.BestMatch?.English} " +
                    $"({observation.Match.Score:0.000}) {reason}");

                saved++;
                RestoreCapturePosition(capture, frameIndex);
                previousStable = true;
            }

            Console.WriteLine(
                $"Scanned frames {startFrame}..{Math.Min(endFrame, startFrame + maxFrames)}, stable runs {stableRuns}, " +
                $"suppressed {suppressedRuns}, suspicious {suspiciousRuns}, saved {saved} to {outputDir}.");
        }

        private static bool IsSuppressedByCollectionScreen(
            VideoCapture capture,
            HeuristicTextPresenceDetector detector,
            OcrRegionType regionType,
            int frameIndex)
        {
            if (regionType != OcrRegionType.Talkatoo)
                return false;

            return HasNearbyDetectorHit(capture, detector, OcrRegionType.MoonGet, frameIndex, searchBackFrames: 45, searchForwardFrames: 30, stride: 5) ||
                HasNearbyDetectorHit(capture, detector, OcrRegionType.StoryMoon, frameIndex, searchBackFrames: 45, searchForwardFrames: 30, stride: 5);
        }

        private static bool HasNearbyDetectorHit(
            VideoCapture capture,
            HeuristicTextPresenceDetector detector,
            OcrRegionType regionType,
            int frameIndex,
            int searchBackFrames,
            int searchForwardFrames,
            int stride)
        {
            var bounds = DetectionBounds(regionType);
            var start = Math.Max(0, frameIndex - searchBackFrames);
            var end = Math.Max(start, frameIndex + searchForwardFrames);
            var step = Math.Max(1, stride);

            for (var nearbyFrame = start; nearbyFrame <= end; nearbyFrame += step)
            {
                using var frame = TryReadFrame(capture, nearbyFrame);
                if (frame == null)
                    continue;

                using var detectionCrop = new Mat(frame, bounds);
                if (detector.Detect(regionType, detectionCrop).Present)
                    return true;
            }

            return false;
        }

        private static string? SuspiciousReason(MatchResult match, double minimumScore)
        {
            if (match.Score >= minimumScore)
                return null;

            if (match.BestMatch == null)
                return "unmatched";

            return "low-score";
        }

        private static Observation Observe(
            OnnxOcrService ocr,
            MoonMatcher matcher,
            List<Moon> candidates,
            Mat ocrCrop,
            int frameIndex)
        {
            var text = ocr.ReadText(ocrCrop);
            return new Observation(frameIndex, text, matcher.Match(text, candidates));
        }

        private static Observation BestNearbyObservation(
            VideoCapture capture,
            HeuristicTextPresenceDetector detector,
            OnnxOcrService ocr,
            MoonMatcher matcher,
            List<Moon> candidates,
            OcrRegionType regionType,
            Rect detectionBounds,
            Rect ocrBounds,
            int eventFrame,
            int searchBackFrames,
            int searchForwardFrames,
            int stride)
        {
            var best = new Observation(eventFrame, string.Empty, new MatchResult());
            var start = Math.Max(0, eventFrame - searchBackFrames);
            var end = Math.Max(start, eventFrame + searchForwardFrames);
            var step = Math.Max(1, stride);

            for (var frameIndex = start; frameIndex <= end; frameIndex += step)
            {
                using var frame = ReadFrame(capture, frameIndex);
                using var detectionCrop = new Mat(frame, detectionBounds);
                var presence = detector.Detect(regionType, detectionCrop);
                if (!presence.Present)
                    continue;

                using var ocrCrop = new Mat(frame, ocrBounds);
                var observation = Observe(ocr, matcher, candidates, ocrCrop, frameIndex);
                if (observation.Match.Score < best.Match.Score)
                    continue;

                best = observation;
            }

            return best;
        }

        private static Mat ReadFrame(VideoCapture capture, int frameIndex)
        {
            capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
            var frame = new Mat();
            if (!capture.Read(frame) || frame.Empty())
            {
                frame.Dispose();
                throw new InvalidOperationException($"Could not read frame {frameIndex}.");
            }

            return frame;
        }

        private static Mat? TryReadFrame(VideoCapture capture, int frameIndex)
        {
            capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
            var frame = new Mat();
            if (capture.Read(frame) && !frame.Empty())
                return frame;

            frame.Dispose();
            return null;
        }

        private static void RestoreCapturePosition(VideoCapture capture, int frameIndex)
        {
            capture.Set(VideoCaptureProperties.PosFrames, frameIndex + 1);
        }

        private static Rect DetectionBounds(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.Talkatoo => TalkatooBounds,
                OcrRegionType.MoonGet => MoonGetDetectionBounds,
                OcrRegionType.StoryMoon => StoryMoonBounds,
                _ => throw new ArgumentOutOfRangeException(nameof(regionType))
            };
        }

        private static Rect OcrBounds(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.Talkatoo => TalkatooBounds,
                OcrRegionType.MoonGet => MoonGetOcrBounds,
                OcrRegionType.StoryMoon => StoryMoonBounds,
                _ => throw new ArgumentOutOfRangeException(nameof(regionType))
            };
        }

        private static (int StableFrameCount, int StableImageMaxHammingDistance) Stability(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.Talkatoo => (2, 64),
                OcrRegionType.MoonGet => (3, 64),
                OcrRegionType.StoryMoon => (3, 64),
                _ => throw new ArgumentOutOfRangeException(nameof(regionType))
            };
        }

        private static int SearchBackFrames(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.MoonGet => 360,
                OcrRegionType.StoryMoon => 180,
                _ => 45
            };
        }

        private static int SearchForwardFrames(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.MoonGet => 30,
                OcrRegionType.StoryMoon => 30,
                _ => 24
            };
        }

        private static List<Moon> Candidates(
            MoonRepository repo,
            OcrRegionType regionType,
            RunSettings settings,
            string? kingdom)
        {
            if (!string.IsNullOrWhiteSpace(kingdom))
            {
                return regionType == OcrRegionType.Talkatoo
                    ? repo.GetTalkatooCandidates(kingdom, settings)
                    : repo.GetCollectionCandidates(kingdom, settings);
            }

            return repo.Query(new MoonQueryOptions
            {
                IncludeStory = regionType != OcrRegionType.Talkatoo,
                IncludeNonStory = true,
                IncludeHintArt = true,
                IncludePostGameKingdoms = true
            });
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

        private static void WriteEvent(
            string outputDir,
            Mat frame,
            Mat detectionCrop,
            Mat ocrCrop,
            int frameIndex,
            OcrRegionType regionType,
            TextPresenceResult presence,
            string text,
            MatchResult match,
            string reason)
        {
            var best = match.BestMatch == null
                ? "unmatched"
                : $"{match.BestMatch.Kingdom}_{match.BestMatch.Id}_{Sanitize(match.BestMatch.English)}";
            var prefix = $"{regionType}_{frameIndex:D7}_conf_{presence.Confidence:0.000}_{reason}_{best}";
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_frame.jpg"), frame);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_detect.jpg"), detectionCrop);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_ocr.jpg"), ocrCrop);
            File.WriteAllText(Path.Combine(outputDir, $"{prefix}.txt"), text);
        }

        private static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private static string Sanitize(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = value
                .Select(ch => invalid.Contains(ch) || char.IsWhiteSpace(ch) ? '_' : ch)
                .ToArray();

            return new string(chars).Trim('_');
        }

        private readonly record struct Observation(int Frame, string Text, MatchResult Match);
    }
}
