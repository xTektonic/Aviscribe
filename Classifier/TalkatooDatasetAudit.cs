using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class TalkatooDatasetAudit
    {
        public static void Run(string dataRoot, string outputDir, int maxPerBucket)
        {
            Directory.CreateDirectory(outputDir);

            var detector = new HeuristicTextPresenceDetector();
            var falseNegativeCount = SaveErrors(
                Path.Combine(dataRoot, "ClassifiedData", "Talkatoo", "Good"),
                Path.Combine(outputDir, "FalseNegative"),
                expectedPresent: true,
                detector,
                maxPerBucket);

            var falsePositiveCount = SaveErrors(
                Path.Combine(dataRoot, "ClassifiedData", "Talkatoo", "Bad"),
                Path.Combine(outputDir, "FalsePositive"),
                expectedPresent: false,
                detector,
                maxPerBucket);

            Console.WriteLine($"Saved {falseNegativeCount} false negatives and {falsePositiveCount} false positives to {outputDir}");
        }

        private static int SaveErrors(
            string inputDir,
            string outputDir,
            bool expectedPresent,
            ITextPresenceDetector detector,
            int maxSaved)
        {
            if (!Directory.Exists(inputDir))
                return 0;

            Directory.CreateDirectory(outputDir);

            var saved = 0;
            foreach (var path in Directory.EnumerateFiles(inputDir, "*.*", SearchOption.TopDirectoryOnly).Where(DatasetInspector.IsImage))
            {
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                    continue;

                var result = detector.Detect(OcrRegionType.Talkatoo, image);
                if (result.Present == expectedPresent)
                    continue;

                var fileName = $"{Path.GetFileNameWithoutExtension(path)}_conf_{result.Confidence:0.000}.jpg";
                Cv2.ImWrite(Path.Combine(outputDir, fileName), image);

                saved++;
                if (saved >= maxSaved)
                    break;
            }

            return saved;
        }
    }
}
