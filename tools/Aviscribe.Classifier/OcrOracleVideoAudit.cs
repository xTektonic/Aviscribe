using Aviscribe.Core;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class OcrOracleVideoAudit
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
            using var frame = new Mat();
            var observations = new List<AuditObservation>();

            var scanned = 0;
            var oraclePositives = 0;
            var detectorPositives = 0;
            var endFrame = maxFrames <= 0 ? int.MaxValue : startFrame + maxFrames;
            capture.Set(VideoCaptureProperties.PosFrames, Math.Max(0, startFrame));

            for (var frameIndex = Math.Max(0, startFrame);
                 frameIndex <= endFrame;
                 frameIndex++)
            {
                if (!capture.Grab())
                    break;

                if ((frameIndex - startFrame) % stride != 0)
                    continue;

                if (!capture.Retrieve(frame) || frame.Empty())
                    break;

                scanned++;

                using var detectionCrop = new Mat(frame, detectionBounds);
                using var ocrCrop = new Mat(frame, ocrBounds);
                var presence = detector.Detect(regionType, detectionCrop);
                var text = ocr.ReadText(ocrCrop);
                var match = matcher.Match(text, candidates);
                var oraclePositive = match.Score >= minimumScore;

                if (presence.Present)
                    detectorPositives++;

                if (oraclePositive)
                    oraclePositives++;

                observations.Add(new AuditObservation(frameIndex, presence, oraclePositive, text, match));
            }

            using var writer = new StreamWriter(Path.Combine(outputDir, "ocr-oracle-audit.csv"));
            writer.AutoFlush = true;
            writer.WriteLine("frame,region,kingdom,detector_present,confidence,oracle_positive,covered_by_nearby_detector,covered_by_nearby_oracle,ocr_text,best_id,best_kingdom,best_english,score,ambiguous,issue");

            var misses = 0;
            var uncoveredMisses = 0;
            var falsePositives = 0;
            var storyScreenMatches = 0;
            var uncoveredStoryScreens = 0;
            var savedMisses = 0;
            var savedFalsePositives = 0;
            var coverageFrames = NearbyCoverageFrames(regionType);

            foreach (var observation in observations)
            {
                var issue = string.Empty;
                var coveredByNearbyDetector = observation.OraclePositive &&
                    (observations.Any(candidate =>
                            candidate.Presence.Present &&
                            Math.Abs(candidate.FrameIndex - observation.FrameIndex) <= coverageFrames) ||
                        (!observation.Presence.Present &&
                         HasNearbyDetectorHit(
                             capture,
                             detector,
                             detectionBounds,
                             regionType,
                             observation.FrameIndex,
                             coverageFrames,
                             Math.Min(stride, DetectorLookaroundStride(regionType)))));
                var coveredByNearbyOracle = observation.Presence.Present &&
                    observations.Any(candidate =>
                        candidate.OraclePositive &&
                        Math.Abs(candidate.FrameIndex - observation.FrameIndex) <= coverageFrames);

                if (IsStoryMatchInMoonGetAudit(regionType, observation))
                {
                    var coveredByStoryDetector = HasNearbyDetectorHit(
                        capture,
                        detector,
                        DetectionBounds(OcrRegionType.StoryMoon),
                        OcrRegionType.StoryMoon,
                        observation.FrameIndex,
                        NearbyCoverageFrames(OcrRegionType.MoonGet),
                        Math.Min(stride, DetectorLookaroundStride(OcrRegionType.StoryMoon)));

                    issue = coveredByStoryDetector ? "story-screen-covered" : "story-screen-uncovered";
                    coveredByNearbyDetector = coveredByStoryDetector;
                    storyScreenMatches++;

                    if (!coveredByStoryDetector)
                        uncoveredStoryScreens++;
                }
                else if (observation.OraclePositive && !observation.Presence.Present)
                {
                    issue = coveredByNearbyDetector ? "miss-covered" : "miss-uncovered";
                    misses++;

                    if (!coveredByNearbyDetector)
                        uncoveredMisses++;
                }
                else if (observation.Presence.Present && !observation.OraclePositive)
                {
                    issue = coveredByNearbyOracle ? "false-positive-covered" : "false-positive";

                    if (!coveredByNearbyOracle)
                        falsePositives++;
                }

                if (issue.Length == 0)
                    continue;

                writer.WriteLine(
                    $"{observation.FrameIndex},{regionType},{kingdom ?? "*"}," +
                    $"{observation.Presence.Present},{observation.Presence.Confidence:0.000},{observation.OraclePositive}," +
                    $"{coveredByNearbyDetector},{coveredByNearbyOracle},{Csv(observation.Text)},{observation.Match.BestMatch?.Id},{observation.Match.BestMatch?.Kingdom}," +
                    $"{Csv(observation.Match.BestMatch?.English ?? string.Empty)},{observation.Match.Score:0.000},{observation.Match.IsAmbiguous},{issue}");

                if (((issue == "miss-uncovered" || issue == "story-screen-uncovered") && savedMisses < maxSaved) ||
                    (issue == "false-positive" && savedFalsePositives < maxSaved))
                {
                    using var issueFrame = ReadFrame(capture, observation.FrameIndex);
                    using var issueDetectionCrop = new Mat(issueFrame, detectionBounds);
                    using var issueOcrCrop = new Mat(issueFrame, ocrBounds);
                    WriteIssue(
                        outputDir,
                        issueFrame,
                        issueDetectionCrop,
                        issueOcrCrop,
                        observation.FrameIndex,
                        regionType,
                        observation.Presence,
                        observation.Text,
                        observation.Match,
                        issue);

                    if (issue == "miss-uncovered" || issue == "story-screen-uncovered")
                        savedMisses++;
                    else
                        savedFalsePositives++;
                }

                Console.WriteLine(
                    $"{observation.FrameIndex:D7} {issue} {regionType}: detector {observation.Presence.Present} ({observation.Presence.Confidence:0.000}), " +
                    $"covered detector {coveredByNearbyDetector}, covered oracle {coveredByNearbyOracle}, ocr \"{observation.Text}\" -> " +
                    $"{observation.Match.BestMatch?.Id} {observation.Match.BestMatch?.Kingdom} {observation.Match.BestMatch?.English} " +
                    $"({observation.Match.Score:0.000}){(observation.Match.IsAmbiguous ? " ambiguous" : string.Empty)}");
            }

            Console.WriteLine(
                $"OCR oracle scanned {scanned} sampled frame(s) from {startFrame}..{Math.Min(endFrame, startFrame + maxFrames)}. " +
                $"Oracle positives {oraclePositives}, detector positives {detectorPositives}, misses {misses}, " +
                $"uncovered misses {uncoveredMisses}, story screens {storyScreenMatches}, uncovered story screens {uncoveredStoryScreens}, " +
                $"false positives {falsePositives}, saved {savedMisses} uncovered miss sample(s) and " +
                $"{savedFalsePositives} false-positive sample(s) to {outputDir}.");
        }

        private static bool IsStoryMatchInMoonGetAudit(OcrRegionType regionType, AuditObservation observation)
        {
            return regionType == OcrRegionType.MoonGet &&
                observation.OraclePositive &&
                !observation.Presence.Present &&
                observation.Match.BestMatch?.IsStory == true;
        }

        private static int NearbyCoverageFrames(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.Talkatoo => 45,
                OcrRegionType.MoonGet => 360,
                OcrRegionType.StoryMoon => 120,
                _ => throw new ArgumentOutOfRangeException(nameof(regionType))
            };
        }

        private static int DetectorLookaroundStride(OcrRegionType regionType)
        {
            return regionType switch
            {
                OcrRegionType.Talkatoo => 3,
                OcrRegionType.MoonGet => 10,
                OcrRegionType.StoryMoon => 6,
                _ => throw new ArgumentOutOfRangeException(nameof(regionType))
            };
        }

        private static bool HasNearbyDetectorHit(
            VideoCapture capture,
            HeuristicTextPresenceDetector detector,
            Rect detectionBounds,
            OcrRegionType regionType,
            int frameIndex,
            int coverageFrames,
            int stride)
        {
            var start = Math.Max(0, frameIndex - coverageFrames);
            var end = frameIndex + coverageFrames;
            var step = Math.Max(1, stride);

            for (var nearbyFrame = start; nearbyFrame <= end; nearbyFrame += step)
            {
                if (nearbyFrame == frameIndex)
                    continue;

                using var frame = TryReadFrame(capture, nearbyFrame);
                if (frame == null)
                    continue;

                using var detectionCrop = new Mat(frame, detectionBounds);
                if (detector.Detect(regionType, detectionCrop).Present)
                    return true;
            }

            return false;
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

        private static void WriteIssue(
            string outputDir,
            Mat frame,
            Mat detectionCrop,
            Mat ocrCrop,
            int frameIndex,
            OcrRegionType regionType,
            TextPresenceResult presence,
            string text,
            MatchResult match,
            string issue)
        {
            var best = match.BestMatch == null
                ? "unmatched"
                : $"{match.BestMatch.Kingdom}_{match.BestMatch.Id}_{Sanitize(match.BestMatch.English)}";
            var prefix = $"{regionType}_{frameIndex:D7}_{issue}_conf_{presence.Confidence:0.000}_{best}";
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

        private readonly record struct AuditObservation(
            int FrameIndex,
            TextPresenceResult Presence,
            bool OraclePositive,
            string Text,
            MatchResult Match);
    }
}
