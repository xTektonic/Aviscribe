using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class TalkatooStaticGateAudit
    {
        private const int ExpectedNormalYellowPositives = 144;
        private const int ExpectedLegacyWhiteOverlays = 36;
        private const int ExpectedNegatives = 36_248;

        public static void Run(string dataRoot)
        {
            var positiveDirectory = Path.Combine(
                dataRoot,
                "ClassifiedData",
                "Talkatoo",
                "Good");
            var negativeDirectory = Path.Combine(
                dataRoot,
                "ClassifiedData",
                "Talkatoo",
                "Bad");

            var normalTotal = 0;
            var normalDetected = 0;
            var legacyTotal = 0;
            var legacyRejected = 0;
            var missedNormal = new List<string>();
            var acceptedLegacy = new List<string>();

            foreach (var path in EnumerateImages(positiveDirectory))
            {
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                    continue;

                var analysis = TalkatooStaticGate.Analyze(image);
                var isNormalYellowPrompt = analysis.TotalYellowPixels >= 500;

                if (isNormalYellowPrompt)
                {
                    normalTotal++;
                    if (analysis.Present)
                        normalDetected++;
                    else
                        missedNormal.Add(Path.GetFileName(path));
                }
                else
                {
                    legacyTotal++;
                    if (!analysis.Present)
                        legacyRejected++;
                    else
                        acceptedLegacy.Add(Path.GetFileName(path));
                }
            }

            var negativeTotal = 0;
            var negativeRejected = 0;

            foreach (var path in EnumerateImages(negativeDirectory))
            {
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                    continue;

                negativeTotal++;
                if (!TextDetection.HasTalkatooText(image))
                    negativeRejected++;
            }

            Console.WriteLine(
                $"Talkatoo static gate audit: normal yellow positives " +
                $"{normalDetected}/{normalTotal}; legacy white overlays rejected " +
                $"{legacyRejected}/{legacyTotal}; labeled-negative detections " +
                $"{negativeTotal - negativeRejected}/{negativeTotal}.");

            var failures = new List<string>();
            if (normalTotal != ExpectedNormalYellowPositives)
            {
                failures.Add(
                    $"expected {ExpectedNormalYellowPositives} normal yellow positives, " +
                    $"classified {normalTotal}");
            }

            if (legacyTotal != ExpectedLegacyWhiteOverlays)
            {
                failures.Add(
                    $"expected {ExpectedLegacyWhiteOverlays} legacy white overlays, " +
                    $"classified {legacyTotal}");
            }

            if (negativeTotal != ExpectedNegatives)
            {
                failures.Add(
                    $"expected {ExpectedNegatives} negatives, found {negativeTotal}");
            }

            if (normalDetected != normalTotal)
            {
                failures.Add(
                    $"missed {normalTotal - normalDetected} normal yellow positives " +
                    $"({string.Join(", ", missedNormal.Take(20))})");
            }

            if (legacyRejected != legacyTotal)
            {
                failures.Add(
                    $"accepted {legacyTotal - legacyRejected} legacy white overlays " +
                    $"({string.Join(", ", acceptedLegacy.Take(20))})");
            }

            if (failures.Count > 0)
            {
                throw new InvalidOperationException(
                    $"Talkatoo static gate audit failed: {string.Join("; ", failures)}.");
            }
        }

        private static IEnumerable<string> EnumerateImages(string directory)
        {
            return Directory
                .EnumerateFiles(directory)
                .Where(DatasetInspector.IsImage)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase);
        }
    }
}
