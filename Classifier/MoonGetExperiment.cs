using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class MoonGetExperiment
    {
        public static void PrintSummary(string dataRoot)
        {
            var positives = Load(Path.Combine(dataRoot, "ClassifiedData", "MoonGet", "Good")).ToArray();
            var negatives = Load(Path.Combine(dataRoot, "ClassifiedData", "MoonGet", "Bad"), maxSamples: 8000).ToArray();

            Console.WriteLine($"Loaded {positives.Length} positives and {negatives.Length} negatives");
            PrintDistribution("Good", positives.Select(x => x.Metrics).ToArray());
            PrintDistribution("Bad", negatives.Select(x => x.Metrics).ToArray());

            var candidates = new[]
            {
                new Candidate(1200, 0.025, 800, 120, 180, 10, 0.18, 0.58, 4, 500, 120, 300, 80),
                new Candidate(1800, 0.035, 1400, 160, 240, 12, 0.18, 0.58, 6, 900, 160, 500, 120),
                new Candidate(2400, 0.045, 2000, 210, 300, 14, 0.18, 0.60, 8, 1200, 200, 700, 160),
                new Candidate(3000, 0.055, 2600, 260, 360, 16, 0.16, 0.62, 10, 1600, 240, 900, 200),
                new Candidate(3800, 0.065, 3400, 320, 430, 18, 0.16, 0.64, 12, 2200, 280, 1100, 240),
                new Candidate(2400, 0.045, 2000, 210, 300, 14, 0.18, 0.60, 10, 1800, 240, 900, 200),
                new Candidate(2400, 0.045, 2000, 210, 300, 14, 0.18, 0.60, 14, 2200, 280, 1100, 240),
                new Candidate(3000, 0.055, 2600, 260, 360, 16, 0.16, 0.62, 14, 2400, 300, 1300, 280)
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
                    $"pix>={candidate.MinWhitePixels}, ratio>={candidate.MinWhiteRatio:0.000}, " +
                    $"band>={candidate.MinBandScore:0}, cols>={candidate.MinActiveColumns}, " +
                    $"span>={candidate.MinSpanWidth}, rows>={candidate.MinBandRows}, " +
                    $"center {candidate.MinCenter:0.00}..{candidate.MaxCenter:0.00}, " +
                    $"comps>={candidate.MinTextComponents}, compArea>={candidate.MinComponentArea}, compSpan>={candidate.MinComponentSpan}, " +
                    $"outline>={candidate.MinOutlinedPixels}, outlineCols>={candidate.MinOutlinedColumns}");
            }

            var reviewCandidate = candidates.First();
            Console.WriteLine("Review candidate misses:");
            foreach (var sample in positives.Where(x => !reviewCandidate.Accept(x.Metrics)).Take(20))
                Console.WriteLine($"  FN {Path.GetFileName(sample.Path)}: {Format(sample.Metrics)}");

            Console.WriteLine("Review candidate false positives:");
            foreach (var sample in negatives.Where(x => reviewCandidate.Accept(x.Metrics)).Take(20))
                Console.WriteLine($"  FP {Path.GetFileName(sample.Path)}: {Format(sample.Metrics)}");
        }

        internal static MoonGetMetrics Measure(Mat image)
        {
            using var mask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.Black);
            var darkIntegral = BuildDarkIntegral(image);
            var rowCounts = new int[image.Height];
            var whitePixels = 0;
            var outlinedPixels = 0;
            var outlinedColumns = new bool[image.Width];

            for (var y = 0; y < image.Height; y++)
            {
                var count = 0;
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    if (IsMoonGetTextPixel(r, g, b))
                    {
                        count++;
                        whitePixels++;
                        mask.Set(y, x, 255);

                        if (HasNearbyDarkPixel(darkIntegral, image.Width, image.Height, x, y))
                        {
                            outlinedPixels++;
                            outlinedColumns[x] = true;
                        }
                    }
                }

                rowCounts[y] = count;
            }

            var rowThreshold = Math.Max(34.0, image.Width * 0.04);
            var bestStart = 0;
            var bestEnd = 0;
            var bestScore = 0.0;

            for (var start = 0; start < image.Height; start++)
            {
                var score = 0.0;
                for (var end = start; end < Math.Min(image.Height, start + 42); end++)
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
            var left = image.Width;
            var right = 0;

            for (var x = 0; x < image.Width; x++)
            {
                var count = 0;
                for (var y = bestStart; y < bestEnd; y++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    if (IsMoonGetTextPixel(r, g, b))
                        count++;
                }

                if (count >= 2)
                {
                    activeColumns++;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x + 1);
                }
            }

            var spanWidth = right - (left == image.Width ? right : left);
            var center = bestEnd <= bestStart
                ? 0
                : ((bestStart + bestEnd) / 2.0) / Math.Max(1, image.Height);

            var (componentCount, componentArea, componentSpan) = MeasureTextComponents(mask);

            return new MoonGetMetrics(
                whitePixels,
                whitePixels / (double)Math.Max(1, image.Width * image.Height),
                bestScore,
                bestEnd - bestStart,
                center,
                activeColumns,
                left == image.Width ? 0 : left,
                right,
                spanWidth,
                componentCount,
                componentArea,
                componentSpan,
                outlinedPixels,
                outlinedColumns.Count(x => x));
        }

        private static int[,] BuildDarkIntegral(Mat image)
        {
            var integral = new int[image.Height + 1, image.Width + 1];

            for (var y = 0; y < image.Height; y++)
            {
                var rowSum = 0;
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var max = Math.Max(pixel.Item2, Math.Max(pixel.Item1, pixel.Item0));
                    if (max <= 95)
                        rowSum++;

                    integral[y + 1, x + 1] = integral[y, x + 1] + rowSum;
                }
            }

            return integral;
        }

        private static bool HasNearbyDarkPixel(int[,] darkIntegral, int width, int height, int x, int y)
        {
            var minX = Math.Max(0, x - 2);
            var maxX = Math.Min(width - 1, x + 2);
            var minY = Math.Max(0, y - 2);
            var maxY = Math.Min(height - 1, y + 2);
            var count =
                darkIntegral[maxY + 1, maxX + 1] -
                darkIntegral[minY, maxX + 1] -
                darkIntegral[maxY + 1, minX] +
                darkIntegral[minY, minX];

            return count > 0;
        }

        private static (int Count, int Area, int Span) MeasureTextComponents(Mat mask)
        {
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var componentCount = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);

            var count = 0;
            var areaSum = 0;
            var left = mask.Width;
            var right = 0;

            for (var i = 1; i < componentCount; i++)
            {
                var x = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                var y = stats.Get<int>(i, (int)ConnectedComponentsTypes.Top);
                var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                var area = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);
                var fill = area / (double)Math.Max(1, width * height);

                if (area < 20 || area > 3500)
                    continue;

                if (width < 3 || width > 105 || height < 5 || height > 55)
                    continue;

                if (fill < 0.08 || fill > 0.98)
                    continue;

                if (y > mask.Height * 0.9)
                    continue;

                count++;
                areaSum += area;
                left = Math.Min(left, x);
                right = Math.Max(right, x + width);
            }

            return (count, areaSum, right - (left == mask.Width ? right : left));
        }

        private static bool IsMoonGetTextPixel(int r, int g, int b)
        {
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            return max >= 145 && min >= 115 && max - min <= 75;
        }

        private static Candidate Score(Candidate candidate, Sample[] positives, Sample[] negatives)
        {
            var truePositive = positives.Count(x => candidate.Accept(x.Metrics));
            var falsePositive = negatives.Count(x => candidate.Accept(x.Metrics));

            return candidate with
            {
                TruePositive = truePositive,
                FalsePositive = falsePositive,
                Recall = Ratio(truePositive, positives.Length),
                FalsePositiveRate = Ratio(falsePositive, negatives.Length)
            };
        }

        private static IEnumerable<Sample> Load(string dir, int maxSamples = int.MaxValue)
        {
            if (!Directory.Exists(dir))
                yield break;

            var loaded = 0;
            foreach (var path in Directory.EnumerateFiles(dir).Where(DatasetInspector.IsImage))
            {
                if (loaded >= maxSamples)
                    yield break;

                using var image = Cv2.ImRead(path);
                if (!image.Empty())
                {
                    loaded++;
                    yield return new Sample(path, Measure(image));
                }
            }
        }

        private static void PrintDistribution(string label, MoonGetMetrics[] metrics)
        {
            Console.WriteLine(label);
            PrintPercentiles("  white pixels", metrics.Select(x => (double)x.WhitePixels).ToArray());
            PrintPercentiles("  white ratio", metrics.Select(x => x.WhiteRatio).ToArray());
            PrintPercentiles("  band score", metrics.Select(x => x.BandScore).ToArray());
            PrintPercentiles("  band rows", metrics.Select(x => (double)x.BandRows).ToArray());
            PrintPercentiles("  center", metrics.Select(x => x.BandCenterRatio).ToArray());
            PrintPercentiles("  active cols", metrics.Select(x => (double)x.ActiveColumns).ToArray());
            PrintPercentiles("  span width", metrics.Select(x => (double)x.SpanWidth).ToArray());
            PrintPercentiles("  text comps", metrics.Select(x => (double)x.TextComponentCount).ToArray());
            PrintPercentiles("  text comp area", metrics.Select(x => (double)x.TextComponentArea).ToArray());
            PrintPercentiles("  text comp span", metrics.Select(x => (double)x.TextComponentSpan).ToArray());
            PrintPercentiles("  outlined pixels", metrics.Select(x => (double)x.OutlinedPixels).ToArray());
            PrintPercentiles("  outlined cols", metrics.Select(x => (double)x.OutlinedColumns).ToArray());
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

        private static string Format(MoonGetMetrics metrics)
        {
            return
                $"pix {metrics.WhitePixels}, ratio {metrics.WhiteRatio:0.000}, band {metrics.BandScore:0}, " +
                $"rows {metrics.BandRows}, center {metrics.BandCenterRatio:0.00}, cols {metrics.ActiveColumns}, " +
                $"span {metrics.SpanWidth}, comps {metrics.TextComponentCount}, compArea {metrics.TextComponentArea}, " +
                $"compSpan {metrics.TextComponentSpan}, outline {metrics.OutlinedPixels}, outlineCols {metrics.OutlinedColumns}, " +
                $"left {metrics.Left}, right {metrics.Right}";
        }

        private sealed record Candidate(
            int MinWhitePixels,
            double MinWhiteRatio,
            double MinBandScore,
            int MinActiveColumns,
            int MinSpanWidth,
            int MinBandRows,
            double MinCenter,
            double MaxCenter,
            int MinTextComponents,
            int MinComponentArea,
            int MinComponentSpan,
            int MinOutlinedPixels,
            int MinOutlinedColumns,
            int TruePositive = 0,
            int FalsePositive = 0,
            double Recall = 0,
            double FalsePositiveRate = 0)
        {
            public bool Accept(MoonGetMetrics metrics)
            {
                return
                    metrics.WhitePixels >= MinWhitePixels &&
                    metrics.WhiteRatio >= MinWhiteRatio &&
                    metrics.BandScore >= MinBandScore &&
                    metrics.ActiveColumns >= MinActiveColumns &&
                    metrics.SpanWidth >= MinSpanWidth &&
                    metrics.BandRows >= MinBandRows &&
                    metrics.BandCenterRatio >= MinCenter &&
                    metrics.BandCenterRatio <= MaxCenter &&
                    metrics.TextComponentCount >= MinTextComponents &&
                    metrics.TextComponentArea >= MinComponentArea &&
                    metrics.TextComponentSpan >= MinComponentSpan &&
                    metrics.OutlinedPixels >= MinOutlinedPixels &&
                    metrics.OutlinedColumns >= MinOutlinedColumns;
            }
        }

        private sealed record Sample(string Path, MoonGetMetrics Metrics);
    }

    internal sealed record MoonGetMetrics(
        int WhitePixels,
        double WhiteRatio,
        double BandScore,
        int BandRows,
        double BandCenterRatio,
        int ActiveColumns,
        int Left,
        int Right,
        int SpanWidth,
        int TextComponentCount,
        int TextComponentArea,
        int TextComponentSpan,
        int OutlinedPixels,
        int OutlinedColumns);
}
