using Aviscribe.Core;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class VideoOcrProbe
    {
        private static readonly Rect TalkatooOcrBounds = new(666, 862, 649, 48);
        private static readonly Rect MoonGetOcrBounds = new(490, 797, 930, 60);
        private static readonly Rect StoryMoonBounds = new(450, 820, 1100, 150);

        public static void Print(string videoPath, IEnumerable<ProbeRequest> requests)
        {
            Print(videoPath, requests, assertExpectedMatches: false);
        }

        public static void AssertMatches(string videoPath, IEnumerable<ProbeRequest> requests)
        {
            Print(videoPath, requests, assertExpectedMatches: true);
        }

        private static void Print(string videoPath, IEnumerable<ProbeRequest> requests, bool assertExpectedMatches)
        {
            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            using var ocr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var repo = MoonRepository.LoadDefault();
            var settings = new RunSettings();
            var matcher = new MoonMatcher(repo, settings.InputLanguage);
            using var frame = new Mat();
            var failures = new List<ProbeRequest>();

            foreach (var request in requests)
            {
                capture.Set(VideoCaptureProperties.PosFrames, request.Frame);
                if (!capture.Read(frame) || frame.Empty())
                {
                    Console.WriteLine($"{request.Name}: could not read frame {request.Frame}");
                    continue;
                }

                var bounds = request.RegionType switch
                {
                    OcrRegionType.Talkatoo => TalkatooOcrBounds,
                    OcrRegionType.MoonGet => MoonGetOcrBounds,
                    OcrRegionType.StoryMoon => StoryMoonBounds,
                    _ => throw new ArgumentOutOfRangeException(nameof(request.RegionType))
                };

                using var crop = new Mat(frame, bounds);
                var text = ocr.ReadText(crop);
                var result = request.RegionType == OcrRegionType.Talkatoo
                    ? matcher.MatchTalkatooText(text, request.Kingdom, settings)
                    : matcher.MatchCollectionText(text, request.Kingdom, settings);

                Console.WriteLine($"{request.Name} [{request.RegionType} {request.Kingdom} frame {request.Frame}]");
                Console.WriteLine($"  OCR: {text}");
                Console.WriteLine($"  Best: {result.BestMatch?.Id} {result.BestMatch?.English} ({result.Score:0.000})");
                if (result.IsAmbiguous)
                    Console.WriteLine("  Ambiguous");
                foreach (var candidate in result.Candidates.Take(3))
                    Console.WriteLine($"    {candidate.moon.Id}: {candidate.moon.English} {candidate.score:0.000}");

                if (!assertExpectedMatches || request.ExpectedMoonId == null)
                    continue;

                if (result.BestMatch?.Id == request.ExpectedMoonId && !result.IsAmbiguous)
                    continue;

                failures.Add(request);
            }

            if (failures.Count > 0)
                throw new InvalidOperationException($"{failures.Count} OCR regression expectation(s) failed.");

            if (assertExpectedMatches)
                Console.WriteLine("Video OCR regression passed.");
        }

        public readonly record struct ProbeRequest(
            string Name,
            OcrRegionType RegionType,
            string Kingdom,
            int Frame,
            int? ExpectedMoonId = null);
    }
}
