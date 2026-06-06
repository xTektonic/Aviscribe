using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class DetectorBenchmark
    {
        public static void PrintSummary(string dataRoot)
        {
            Console.WriteLine("Current detector benchmark");
            Benchmark(dataRoot, "Talkatoo", TextDetection.HasTalkatooText);
            Benchmark(dataRoot, "MoonGet", TextDetection.HasMoonText);
        }

        private static void Benchmark(string dataRoot, string region, Func<Mat, bool> detector)
        {
            var goodDir = Path.Combine(dataRoot, "ClassifiedData", region, "Good");
            var badDir = Path.Combine(dataRoot, "ClassifiedData", region, "Bad");

            var good = Evaluate(goodDir, detector);
            var bad = Evaluate(badDir, detector);

            var recall = Ratio(good.Positive, good.Total);
            var falsePositiveRate = Ratio(bad.Positive, bad.Total);

            Console.WriteLine(
                $"  {region}: recall {recall:P2} ({good.Positive}/{good.Total}), " +
                $"false positives {falsePositiveRate:P2} ({bad.Positive}/{bad.Total})");
        }

        private static (int Total, int Positive) Evaluate(string dir, Func<Mat, bool> detector)
        {
            if (!Directory.Exists(dir))
                return (0, 0);

            var total = 0;
            var positive = 0;

            foreach (var path in Directory.EnumerateFiles(dir).Where(DatasetInspector.IsImage))
            {
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                    continue;

                total++;

                if (detector(image))
                    positive++;
            }

            return (total, positive);
        }

        private static double Ratio(int numerator, int denominator)
        {
            return denominator == 0 ? 0 : (double)numerator / denominator;
        }
    }
}
