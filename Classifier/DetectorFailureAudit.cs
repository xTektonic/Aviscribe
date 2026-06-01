using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class DetectorFailureAudit
    {
        public static void Run(string dataRoot, string region, string outputDir, int maxSaved)
        {
            Directory.CreateDirectory(outputDir);

            Func<Mat, bool> detector = region.Equals("MoonGet", StringComparison.OrdinalIgnoreCase)
                ? TextDetection.HasMoonText
                : TextDetection.HasTalkatooText;

            var goodDir = Path.Combine(dataRoot, "ClassifiedData", region, "Good");
            var badDir = Path.Combine(dataRoot, "ClassifiedData", region, "Bad");

            var falseNegatives = SaveFailures(goodDir, outputDir, "fn", detector, expected: true, maxSaved);
            var falsePositives = SaveFailures(badDir, outputDir, "fp", detector, expected: false, maxSaved);

            Console.WriteLine($"{region} failure audit");
            Console.WriteLine($"  false negatives: {falseNegatives.TotalFailures}/{falseNegatives.Total} saved {falseNegatives.Saved}");
            Console.WriteLine($"  false positives: {falsePositives.TotalFailures}/{falsePositives.Total} saved {falsePositives.Saved}");
            Console.WriteLine($"  output: {outputDir}");
        }

        private static FailureSummary SaveFailures(
            string inputDir,
            string outputDir,
            string prefix,
            Func<Mat, bool> detector,
            bool expected,
            int maxSaved)
        {
            if (!Directory.Exists(inputDir))
                return new FailureSummary(0, 0, 0);

            var total = 0;
            var failures = 0;
            var saved = 0;

            foreach (var path in Directory.EnumerateFiles(inputDir).Where(DatasetInspector.IsImage))
            {
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                    continue;

                total++;
                var actual = detector(image);
                if (actual == expected)
                    continue;

                failures++;
                if (saved >= maxSaved)
                    continue;

                var name = $"{prefix}_{saved + 1:D4}_{Path.GetFileName(path)}";
                Cv2.ImWrite(Path.Combine(outputDir, name), image);
                saved++;
            }

            return new FailureSummary(total, failures, saved);
        }

        private readonly record struct FailureSummary(int Total, int TotalFailures, int Saved);
    }
}
