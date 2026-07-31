using Aviscribe.Core;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class VideoEventMiner
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
            int maxRuns,
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
            var matcher = new MoonMatcher(repo, settings.InputLanguage, settings.OutputLanguage);
            var detectionBounds = DetectionBounds(regionType);
            var ocrBounds = OcrBounds(regionType);
            var stability = Stability(regionType);
            var candidates = Candidates(repo, regionType, settings, kingdom);
            using var frame = new Mat();
            using var writer = new StreamWriter(Path.Combine(outputDir, "events.csv"));
            writer.AutoFlush = true;
            var detectionWindow = new Queue<bool>();
            var hashWindow = new Queue<ulong>();
            var previousStable = false;
            var runCount = 0;
            var endFrame = maxFrames <= 0 ? int.MaxValue : startFrame + maxFrames;

            writer.WriteLine("frame,region,kingdom,ocr_text,best_id,best_kingdom,best_english,score,ambiguous");
            capture.Set(VideoCaptureProperties.PosFrames, Math.Max(0, startFrame));

            for (var frameIndex = Math.Max(0, startFrame);
                 frameIndex <= endFrame && runCount < maxRuns;
                 frameIndex++)
            {
                if (!capture.Grab())
                    break;

                if ((frameIndex - startFrame) % stride != 0)
                    continue;

                if (!capture.Retrieve(frame) || frame.Empty())
                    break;

                using var detectionCrop = new Mat(frame, detectionBounds);
                var result = detector.Detect(regionType, detectionCrop);
                detectionWindow.Enqueue(result.Present);
                hashWindow.Enqueue(result.Present ? ImageHash.Compute(detectionCrop) : 0);

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

                using var ocrCrop = new Mat(frame, ocrBounds);
                var text = ocr.ReadText(ocrCrop);
                var match = matcher.Match(text, candidates);
                WriteRun(outputDir, frame, detectionCrop, ocrCrop, frameIndex, regionType, result, text, match);
                writer.WriteLine(
                    $"{frameIndex},{regionType},{kingdom ?? "*"}," +
                    $"{Csv(text)},{match.BestMatch?.Id},{match.BestMatch?.Kingdom}," +
                    $"{Csv(match.BestMatch?.English ?? string.Empty)},{match.Score:0.000},{match.IsAmbiguous}");
                Console.WriteLine(
                    $"{frameIndex:D7} {regionType}: \"{text}\" -> " +
                    $"{match.BestMatch?.Id} {match.BestMatch?.Kingdom} {match.BestMatch?.English} " +
                    $"({match.Score:0.000}){(match.IsAmbiguous ? " ambiguous" : string.Empty)}");

                previousStable = true;
                runCount++;
            }

            Console.WriteLine($"Mined {runCount} {regionType} stable run(s) to {outputDir}.");
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
                OcrRegionType.MoonGet => (2, 64),
                OcrRegionType.StoryMoon => (2, 64),
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

        private static void WriteRun(
            string outputDir,
            Mat frame,
            Mat detectionCrop,
            Mat ocrCrop,
            int frameIndex,
            OcrRegionType regionType,
            TextPresenceResult presence,
            string text,
            MatchResult match)
        {
            var best = match.BestMatch == null
                ? "unmatched"
                : $"{match.BestMatch.Kingdom}_{match.BestMatch.Id}_{Sanitize(match.BestMatch.English)}";
            var prefix = $"{regionType}_{frameIndex:D7}_conf_{presence.Confidence:0.000}_{best}";
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
    }
}
