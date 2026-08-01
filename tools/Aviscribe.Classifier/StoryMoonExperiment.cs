using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class StoryMoonExperiment
    {
        private static readonly Rect ReferenceNameCrop = new(450, 820, 1100, 150);
        private const int ReferenceWidth = 1920;
        private const int ReferenceHeight = 1080;

        public static void PrintSummary(string dataRoot)
        {
            var positives = Load(Path.Combine(dataRoot, "StoryMoons")).ToArray();
            var unknown = Load(Path.Combine(dataRoot, "StoryMoonData"), maxSamples: 800).ToArray();

            Console.WriteLine($"Loaded {positives.Length} positives and {unknown.Length} unknown samples");
            PrintDistribution("Good", positives.Select(x => x.Metrics).ToArray());
            PrintDistribution("Unknown", unknown.Select(x => x.Metrics).ToArray());

            var candidates = new[]
            {
                new Candidate(1800, 0.010, 1200, 150, 260, 10, 0.15, 0.80, 4, 900, 160, 450, 100),
                new Candidate(2400, 0.014, 1800, 190, 300, 12, 0.18, 0.78, 5, 1400, 220, 700, 140),
                new Candidate(3200, 0.018, 2400, 230, 360, 14, 0.20, 0.76, 6, 1900, 260, 1000, 180),
                new Candidate(4200, 0.023, 3400, 280, 420, 16, 0.20, 0.76, 7, 2600, 300, 1300, 220)
            };

            foreach (var candidate in candidates.Select(x => Score(x, positives, unknown)))
            {
                Console.WriteLine(
                    $"recall {candidate.Recall:P2} ({candidate.TruePositive}/{positives.Length}), " +
                    $"unknown positives {candidate.FalsePositiveRate:P2} ({candidate.FalsePositive}/{unknown.Length}) | " +
                    $"pix>={candidate.MinWhitePixels}, ratio>={candidate.MinWhiteRatio:0.000}, band>={candidate.MinBandScore:0}, " +
                    $"cols>={candidate.MinActiveColumns}, span>={candidate.MinSpanWidth}, comps>={candidate.MinTextComponents}, " +
                    $"compArea>={candidate.MinComponentArea}, outline>={candidate.MinOutlinedPixels}");
            }

            var reviewCandidate = Score(candidates[1], positives, unknown);
            Console.WriteLine("Review candidate misses:");
            foreach (var sample in positives.Where(x => !reviewCandidate.Accept(x.Metrics)).Take(20))
                Console.WriteLine($"  FN {Path.GetFileName(sample.Path)}: {Format(sample.Metrics)}");

            Console.WriteLine("Review candidate accepts from unknown:");
            foreach (var sample in unknown.Where(x => reviewCandidate.Accept(x.Metrics)).Take(20))
                Console.WriteLine($"  U+ {Path.GetFileName(sample.Path)}: {Format(sample.Metrics)}");
        }

        public static Rect ScaleNameCrop(int width, int height)
        {
            var xScale = width / (double)ReferenceWidth;
            var yScale = height / (double)ReferenceHeight;

            var x = (int)Math.Round(ReferenceNameCrop.X * xScale);
            var y = (int)Math.Round(ReferenceNameCrop.Y * yScale);
            var w = (int)Math.Round(ReferenceNameCrop.Width * xScale);
            var h = (int)Math.Round(ReferenceNameCrop.Height * yScale);

            x = Math.Clamp(x, 0, Math.Max(0, width - 1));
            y = Math.Clamp(y, 0, Math.Max(0, height - 1));
            w = Math.Clamp(w, 1, width - x);
            h = Math.Clamp(h, 1, height - y);

            return new Rect(x, y, w, h);
        }

        private static Candidate Score(Candidate candidate, Sample[] positives, Sample[] unknown)
        {
            var truePositive = positives.Count(x => candidate.Accept(x.Metrics));
            var falsePositive = unknown.Count(x => candidate.Accept(x.Metrics));

            return candidate with
            {
                TruePositive = truePositive,
                FalsePositive = falsePositive,
                Recall = Ratio(truePositive, positives.Length),
                FalsePositiveRate = Ratio(falsePositive, unknown.Length)
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
                if (image.Empty())
                    continue;

                using var crop = new Mat(image, ScaleNameCrop(image.Width, image.Height));
                loaded++;
                yield return new Sample(path, new StoryMetrics(MoonGetExperiment.Measure(crop), RedRatio(crop)));
            }
        }

        private static double RedRatio(Mat image)
        {
            var width = image.Width;
            var height = image.Height;
            var redPixels = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    if (r >= 145 && g <= 105 && b <= 125 && r >= g + 45 && r >= b + 45)
                        redPixels++;
                }
            }

            return redPixels / (double)Math.Max(1, width * height);
        }

        private static void PrintDistribution(string label, StoryMetrics[] storyMetrics)
        {
            var metrics = storyMetrics.Select(x => x.Text).ToArray();
            Console.WriteLine(label);
            PrintPercentiles("  white pixels", metrics.Select(x => (double)x.WhitePixels).ToArray());
            PrintPercentiles("  white ratio", metrics.Select(x => x.WhiteRatio).ToArray());
            PrintPercentiles("  band score", metrics.Select(x => x.BandScore).ToArray());
            PrintPercentiles("  center", metrics.Select(x => x.BandCenterRatio).ToArray());
            PrintPercentiles("  active cols", metrics.Select(x => (double)x.ActiveColumns).ToArray());
            PrintPercentiles("  span width", metrics.Select(x => (double)x.SpanWidth).ToArray());
            PrintPercentiles("  text comps", metrics.Select(x => (double)x.TextComponentCount).ToArray());
            PrintPercentiles("  comp area", metrics.Select(x => (double)x.TextComponentArea).ToArray());
            PrintPercentiles("  outline", metrics.Select(x => (double)x.OutlinedPixels).ToArray());
            PrintPercentiles("  outline cols", metrics.Select(x => (double)x.OutlinedColumns).ToArray());
            PrintPercentiles("  red ratio", storyMetrics.Select(x => x.RedRatio).ToArray());
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

        private static string Format(StoryMetrics story)
        {
            var metrics = story.Text;
            return
                $"pix {metrics.WhitePixels}, ratio {metrics.WhiteRatio:0.000}, band {metrics.BandScore:0}, " +
                $"center {metrics.BandCenterRatio:0.00}, cols {metrics.ActiveColumns}, span {metrics.SpanWidth}, " +
                $"comps {metrics.TextComponentCount}, compArea {metrics.TextComponentArea}, outline {metrics.OutlinedPixels}, " +
                $"outlineCols {metrics.OutlinedColumns}, red {story.RedRatio:0.000}";
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
            public bool Accept(StoryMetrics story)
            {
                var metrics = story.Text;
                return
                    story.RedRatio >= 0.10 &&
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

        private sealed record Sample(string Path, StoryMetrics Metrics);
        private sealed record StoryMetrics(MoonGetMetrics Text, double RedRatio);
    }
}
