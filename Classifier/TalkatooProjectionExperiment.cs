using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class TalkatooProjectionExperiment
    {
        public static void PrintSummary(string dataRoot)
        {
            var positiveSamples = Load(Path.Combine(dataRoot, "ClassifiedData", "Talkatoo", "Good")).ToArray();
            var negativeSamples = Load(Path.Combine(dataRoot, "ClassifiedData", "Talkatoo", "Bad")).ToArray();
            var positives = positiveSamples.Select(x => x.Metrics).ToArray();
            var negatives = negativeSamples.Select(x => x.Metrics).ToArray();

            Console.WriteLine($"Loaded {positives.Length} positives and {negatives.Length} negatives");
            PrintDistribution("Good", positives);
            PrintDistribution("Bad", negatives);

            var candidates = new[]
            {
                new Candidate(350, 0.015, 250, 45, 80, 20, 18),
                new Candidate(500, 0.025, 450, 65, 110, 32, 28),
                new Candidate(650, 0.035, 700, 85, 145, 48, 42),
                new Candidate(800, 0.050, 1000, 110, 180, 64, 60),
                new Candidate(1000, 0.075, 1500, 140, 230, 90, 85),
                new Candidate(1300, 0.100, 2200, 175, 280, 120, 115),
                new Candidate(650, 0.035, 700, 85, 145, 64, 42),
                new Candidate(650, 0.035, 700, 85, 145, 90, 42),
                new Candidate(800, 0.050, 1000, 110, 180, 90, 60),
                new Candidate(800, 0.050, 1000, 110, 180, 120, 60),
                new Candidate(1000, 0.075, 1500, 140, 230, 120, 85),
                new Candidate(1000, 0.075, 1500, 140, 230, 150, 85)
            }
            .Select(x => Score(x, positives, negatives))
            .OrderBy(x => x.FalsePositiveRate)
            .ThenByDescending(x => x.Recall)
            .ToArray();

            foreach (var candidate in candidates)
            {
                Console.WriteLine(
                    $"recall {candidate.Recall:P2} ({candidate.TruePositive}/{positives.Length}), " +
                    $"fp {candidate.FalsePositiveRate:P3} ({candidate.FalsePositive}/{negatives.Length}) | " +
                    $"pix>={candidate.MinYellowPixels}, ratio>={candidate.MinRatio:0.000}, " +
                    $"band>={candidate.MinBandScore:0}, cols>={candidate.MinActiveColumns}, " +
                    $"span>={candidate.MinSpanWidth}, frag>={candidate.MinFragmentedColumns}, " +
                    $"median>={candidate.MinMedianRow:0}");
            }

            var reviewCandidate = candidates.First();
            Console.WriteLine("Review candidate misses:");
            foreach (var sample in positiveSamples.Where(x => !reviewCandidate.Accept(x.Metrics)).Take(60))
                Console.WriteLine($"  FN {Path.GetFileName(sample.Path)}: {Format(sample.Metrics)}");

            Console.WriteLine("Review candidate false positives:");
            foreach (var sample in negativeSamples.Where(x => reviewCandidate.Accept(x.Metrics)).Take(20))
                Console.WriteLine($"  FP {Path.GetFileName(sample.Path)}: {Format(sample.Metrics)}");
        }

        private static Candidate Score(
            Candidate candidate,
            TalkatooProjectionMetrics[] positives,
            TalkatooProjectionMetrics[] negatives)
        {
            var truePositive = positives.Count(candidate.Accept);
            var falsePositive = negatives.Count(candidate.Accept);

            return candidate with
            {
                TruePositive = truePositive,
                FalsePositive = falsePositive,
                Recall = Ratio(truePositive, positives.Length),
                FalsePositiveRate = Ratio(falsePositive, negatives.Length)
            };
        }

        internal static TalkatooProjectionMetrics Measure(Mat image)
        {
            using var mask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.Black);
            var colorDeltaSum = 0.0;
            var colorDeltaCount = 0;

            for (var y = 0; y < image.Rows; y++)
            {
                for (var x = 0; x < image.Cols; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    if (x >= 80 && IsYellowTextPixel(r, g, b))
                    {
                        mask.Set(y, x, 255);
                        colorDeltaSum += r - g;
                        colorDeltaCount++;
                    }
                }
            }

            return MeasureMask(mask, colorDeltaCount == 0 ? 0 : colorDeltaSum / colorDeltaCount);
        }

        private static TalkatooProjectionMetrics MeasureMask(Mat mask, double meanRMinusG)
        {
            var rowCounts = new int[mask.Height];
            var yellowPixels = 0;
            for (var y = 0; y < mask.Height; y++)
            {
                var count = 0;
                for (var x = 0; x < mask.Width; x++)
                {
                    if (mask.At<byte>(y, x) > 0)
                        count++;
                }

                rowCounts[y] = count;
                yellowPixels += count;
            }

            var rowThreshold = Math.Max(18.0, mask.Width * 0.04);
            var bestStart = 0;
            var bestEnd = 0;
            var bestScore = 0.0;

            for (var start = 0; start < mask.Height; start++)
            {
                var score = 0.0;
                for (var end = start; end < Math.Min(mask.Height, start + 34); end++)
                {
                    score += Math.Max(0, rowCounts[end] - rowThreshold);
                    if (end - start + 1 >= 8 && score > bestScore)
                    {
                        bestScore = score;
                        bestStart = start;
                        bestEnd = end + 1;
                    }
                }
            }

            var activeColumns = 0;
            var longestColumnRun = 0;
            var currentColumnRun = 0;
            var left = mask.Width;
            var right = 0;
            var fragmentedColumns = 0;

            for (var x = 0; x < mask.Width; x++)
            {
                var count = 0;
                var transitions = 0;
                var wasActive = false;

                for (var y = bestStart; y < bestEnd; y++)
                {
                    var active = mask.At<byte>(y, x) > 0;
                    if (active)
                        count++;

                    if (active && !wasActive)
                        transitions++;

                    wasActive = active;
                }

                if (count >= 2)
                {
                    activeColumns++;
                    currentColumnRun++;
                    longestColumnRun = Math.Max(longestColumnRun, currentColumnRun);
                    left = Math.Min(left, x);
                    right = Math.Max(right, x + 1);

                    if (transitions >= 2)
                        fragmentedColumns++;
                }
                else
                {
                    currentColumnRun = 0;
                }
            }

            var activeRows = rowCounts.Where(x => x >= rowThreshold).OrderBy(x => x).ToArray();
            var medianActiveRow = activeRows.Length == 0 ? 0 : activeRows[activeRows.Length / 2];

            return new TalkatooProjectionMetrics(
                yellowPixels,
                yellowPixels / (double)Math.Max(1, mask.Width * mask.Height),
                bestScore,
                activeColumns,
                longestColumnRun,
                left == mask.Width ? 0 : left,
                right,
                right - (left == mask.Width ? right : left),
                rowCounts.Length == 0 ? 0 : rowCounts.Max(),
                medianActiveRow,
                fragmentedColumns,
                meanRMinusG);
        }

        private static string Format(TalkatooProjectionMetrics metrics)
        {
            return
                $"pix {metrics.YellowPixels}, ratio {metrics.YellowRatio:0.000}, band {metrics.BandScore:0}, " +
                $"cols {metrics.ActiveColumns}, span {metrics.SpanWidth}, frag {metrics.FragmentedColumns}, " +
                $"median {metrics.MedianActiveRow:0}, r-g {metrics.MeanRMinusG:0.0}";
        }

        private static IEnumerable<Sample> Load(string dir)
        {
            if (!Directory.Exists(dir))
                yield break;

            foreach (var path in Directory.EnumerateFiles(dir).Where(DatasetInspector.IsImage))
            {
                using var image = Cv2.ImRead(path);
                if (!image.Empty())
                    yield return new Sample(path, Measure(image));
            }
        }

        private static void PrintDistribution(string label, TalkatooProjectionMetrics[] metrics)
        {
            Console.WriteLine(label);
            PrintPercentiles("  yellow pixels", metrics.Select(x => (double)x.YellowPixels).ToArray());
            PrintPercentiles("  yellow ratio", metrics.Select(x => x.YellowRatio).ToArray());
            PrintPercentiles("  band score", metrics.Select(x => x.BandScore).ToArray());
            PrintPercentiles("  active cols", metrics.Select(x => (double)x.ActiveColumns).ToArray());
            PrintPercentiles("  span width", metrics.Select(x => (double)x.SpanWidth).ToArray());
            PrintPercentiles("  fragmented cols", metrics.Select(x => (double)x.FragmentedColumns).ToArray());
            PrintPercentiles("  median row", metrics.Select(x => x.MedianActiveRow).ToArray());
        }

        private static void PrintPercentiles(string label, double[] values)
        {
            if (values.Length == 0)
            {
                Console.WriteLine($"{label}: n/a");
                return;
            }

            Array.Sort(values);
            Console.WriteLine(
                $"{label}: p01 {Percentile(values, 0.01):0.###}, p05 {Percentile(values, 0.05):0.###}, " +
                $"p50 {Percentile(values, 0.50):0.###}, p95 {Percentile(values, 0.95):0.###}, p99 {Percentile(values, 0.99):0.###}");
        }

        private static double Percentile(double[] sortedValues, double percentile)
        {
            var index = Math.Clamp((int)Math.Round((sortedValues.Length - 1) * percentile), 0, sortedValues.Length - 1);
            return sortedValues[index];
        }

        private static double Ratio(int numerator, int denominator)
        {
            return denominator == 0 ? 0 : numerator / (double)denominator;
        }

        private static bool IsYellowTextPixel(int r, int g, int b)
        {
            return
                r >= 145 &&
                g >= 120 &&
                b <= 135 &&
                r >= g - 25 &&
                r <= g + 70 &&
                r >= b + 55 &&
                g >= b + 45;
        }

        private sealed record Candidate(
            int MinYellowPixels,
            double MinRatio,
            double MinBandScore,
            int MinActiveColumns,
            int MinSpanWidth,
            int MinFragmentedColumns,
            double MinMedianRow,
            int TruePositive = 0,
            int FalsePositive = 0,
            double Recall = 0,
            double FalsePositiveRate = 0)
        {
            public bool Accept(TalkatooProjectionMetrics metrics)
            {
                return
                    metrics.YellowPixels >= MinYellowPixels &&
                    metrics.YellowRatio >= MinRatio &&
                    metrics.BandScore >= MinBandScore &&
                    metrics.ActiveColumns >= MinActiveColumns &&
                    metrics.SpanWidth >= MinSpanWidth &&
                    metrics.FragmentedColumns >= MinFragmentedColumns &&
                    metrics.MedianActiveRow >= MinMedianRow;
            }
        }

        private sealed record Sample(string Path, TalkatooProjectionMetrics Metrics);
    }

    internal sealed record TalkatooProjectionMetrics(
        int YellowPixels,
        double YellowRatio,
        double BandScore,
        int ActiveColumns,
        int LongestColumnRun,
        int Left,
        int Right,
        int SpanWidth,
        int PeakRow,
        double MedianActiveRow,
        int FragmentedColumns,
        double MeanRMinusG);
}
