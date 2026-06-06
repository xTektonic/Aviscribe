using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class LinearModelTrainer
    {
        private static readonly ImageFeatureName[] Features =
        [
            ImageFeatureName.Mean,
            ImageFeatureName.StdDev,
            ImageFeatureName.EdgeDensity,
            ImageFeatureName.BrightRatio,
            ImageFeatureName.ActiveRowRatio,
            ImageFeatureName.LongestRowRunRatio,
            ImageFeatureName.ActiveColumnRatio
        ];

        public static void TrainAndWrite(
            string dataRoot,
            string outputPath,
            double minimumRecallTarget,
            double maximumFalsePositiveRate)
        {
            if (!Directory.Exists(dataRoot))
                throw new DirectoryNotFoundException($"Data root does not exist: {dataRoot}");

            var model = new LinearFeatureModel
            {
                Name = "Aviscribe linear feature detector",
                CreatedUtc = DateTime.UtcNow,
                MinimumRecallTarget = minimumRecallTarget,
                MaximumFalsePositiveRate = maximumFalsePositiveRate
            };

            foreach (var regionType in new[] { OcrRegionType.Talkatoo, OcrRegionType.MoonGet })
            {
                var trainingResult = TrainRegion(dataRoot, regionType, minimumRecallTarget);
                PrintResult(trainingResult);

                if (trainingResult.Metrics.Recall >= minimumRecallTarget &&
                    trainingResult.Metrics.FalsePositiveRate <= maximumFalsePositiveRate)
                {
                    model.Regions.Add(trainingResult.Model);
                    Console.WriteLine($"    accepted for runtime export");
                }
                else
                {
                    Console.WriteLine($"    skipped for runtime export");
                }
            }

            model.Save(outputPath);
            Console.WriteLine($"Wrote linear detector model to {outputPath}");
        }

        private static LinearTrainingResult TrainRegion(
            string dataRoot,
            OcrRegionType regionType,
            double minimumRecallTarget)
        {
            var region = regionType.ToString();
            var rows = DatasetManifest.EnumerateRows(dataRoot, includeDimensions: false)
                .Where(r => r.Region == region && (r.Label == "good" || r.Label == "bad"))
                .ToList();

            var samples = LoadSamples(dataRoot, rows, region);
            var train = samples.Where((_, index) => index % 5 != 0).ToList();
            var validation = samples.Where((_, index) => index % 5 == 0).ToList();

            if (train.Count == 0 || validation.Count == 0)
                return LinearTrainingResult.Empty(regionType);

            var normalization = ComputeNormalization(train);
            var weights = new double[Features.Length];
            var bias = 0.0;
            var positiveCount = Math.Max(1, train.Count(s => s.IsPositive));
            var negativeCount = Math.Max(1, train.Count - positiveCount);
            var positiveWeight = Math.Min(negativeCount / (double)positiveCount, 50);
            const double learningRate = 0.05;
            const double l2 = 0.0005;

            for (var epoch = 0; epoch < 300; epoch++)
            {
                var gradient = new double[weights.Length];
                var biasGradient = 0.0;

                foreach (var sample in train)
                {
                    var x = ToVector(sample.Features, normalization);
                    var probability = Sigmoid(Dot(weights, x) + bias);
                    var sampleWeight = sample.IsPositive ? positiveWeight : 1.0;
                    var error = (probability - (sample.IsPositive ? 1.0 : 0.0)) * sampleWeight;

                    for (var i = 0; i < gradient.Length; i++)
                        gradient[i] += error * x[i];

                    biasGradient += error;
                }

                for (var i = 0; i < weights.Length; i++)
                {
                    gradient[i] = (gradient[i] / train.Count) + (l2 * weights[i]);
                    weights[i] -= learningRate * gradient[i];
                }

                bias -= learningRate * biasGradient / train.Count;
            }

            var threshold = ChooseThreshold(validation, normalization, weights, bias, minimumRecallTarget);
            var metrics = Evaluate(validation, normalization, weights, bias, threshold);

            return new LinearTrainingResult(
                new LinearFeatureRegionModel
                {
                    RegionType = regionType,
                    Features = Features.ToList(),
                    Means = normalization.Means.ToList(),
                    StandardDeviations = normalization.StandardDeviations.ToList(),
                    Weights = weights.ToList(),
                    Bias = bias,
                    Threshold = threshold
                },
                metrics);
        }

        private static List<LinearSample> LoadSamples(string dataRoot, IReadOnlyList<DatasetRow> rows, string region)
        {
            var samples = new LinearSample?[rows.Count];
            var processed = 0;

            Parallel.For(
                0,
                rows.Count,
                new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1) },
                i =>
                {
                    var path = Path.Combine(dataRoot, rows[i].RelativePath);
                    using var image = Cv2.ImRead(path);
                    if (!image.Empty())
                        samples[i] = new LinearSample(rows[i].Label == "good", ImageFeatureExtractor.Extract(image));

                    var done = Interlocked.Increment(ref processed);
                    if (done % 1000 == 0)
                        Console.WriteLine($"  {region}: loaded {done}/{rows.Count}");
                });

            return samples.Where(sample => sample.HasValue).Select(sample => sample!.Value).ToList();
        }

        private static Normalization ComputeNormalization(IReadOnlyList<LinearSample> samples)
        {
            var means = new double[Features.Length];
            var stdDevs = new double[Features.Length];

            foreach (var sample in samples)
            {
                var raw = ToRawVector(sample.Features);
                for (var i = 0; i < raw.Length; i++)
                    means[i] += raw[i];
            }

            for (var i = 0; i < means.Length; i++)
                means[i] /= samples.Count;

            foreach (var sample in samples)
            {
                var raw = ToRawVector(sample.Features);
                for (var i = 0; i < raw.Length; i++)
                    stdDevs[i] += Math.Pow(raw[i] - means[i], 2);
            }

            for (var i = 0; i < stdDevs.Length; i++)
                stdDevs[i] = Math.Sqrt(stdDevs[i] / samples.Count);

            return new Normalization(means, stdDevs);
        }

        private static double ChooseThreshold(
            IReadOnlyList<LinearSample> validation,
            Normalization normalization,
            IReadOnlyList<double> weights,
            double bias,
            double minimumRecallTarget)
        {
            var predictions = validation
                .Select(sample => new Prediction(Predict(sample.Features, normalization, weights, bias), sample.IsPositive))
                .OrderBy(prediction => prediction.Probability)
                .ToList();

            var best = (Threshold: 0.5, FalsePositiveRate: double.MaxValue, Precision: 0.0, F4: 0.0);

            foreach (var threshold in predictions.Select(p => p.Probability).Distinct())
            {
                var metrics = EvaluatePredictions(predictions, threshold);
                if (metrics.Recall < minimumRecallTarget)
                    continue;

                if (metrics.FalsePositiveRate < best.FalsePositiveRate ||
                    (Math.Abs(metrics.FalsePositiveRate - best.FalsePositiveRate) < 0.0000001 && metrics.Precision > best.Precision))
                {
                    best = (threshold, metrics.FalsePositiveRate, metrics.Precision, metrics.F4Score);
                }
            }

            if (!double.IsFinite(best.FalsePositiveRate))
            {
                return predictions
                    .Select(p => p.Probability)
                    .Distinct()
                    .OrderByDescending(threshold => EvaluatePredictions(predictions, threshold).F4Score)
                    .FirstOrDefault(0.5);
            }

            return best.Threshold;
        }

        private static LinearMetrics Evaluate(
            IReadOnlyList<LinearSample> validation,
            Normalization normalization,
            IReadOnlyList<double> weights,
            double bias,
            double threshold)
        {
            var predictions = validation
                .Select(sample => new Prediction(Predict(sample.Features, normalization, weights, bias), sample.IsPositive))
                .ToList();

            return EvaluatePredictions(predictions, threshold);
        }

        private static LinearMetrics EvaluatePredictions(IReadOnlyList<Prediction> predictions, double threshold)
        {
            var truePositive = 0;
            var falsePositive = 0;
            var trueNegative = 0;
            var falseNegative = 0;

            foreach (var prediction in predictions)
            {
                var predictedPositive = prediction.Probability >= threshold;

                if (prediction.IsPositive && predictedPositive)
                    truePositive++;
                else if (!prediction.IsPositive && predictedPositive)
                    falsePositive++;
                else if (!prediction.IsPositive)
                    trueNegative++;
                else
                    falseNegative++;
            }

            return LinearMetrics.Create(truePositive, falsePositive, trueNegative, falseNegative);
        }

        private static double Predict(
            ImageFeatures features,
            Normalization normalization,
            IReadOnlyList<double> weights,
            double bias)
        {
            return Sigmoid(Dot(weights, ToVector(features, normalization)) + bias);
        }

        private static double[] ToVector(ImageFeatures features, Normalization normalization)
        {
            var raw = ToRawVector(features);
            var vector = new double[raw.Length];

            for (var i = 0; i < raw.Length; i++)
                vector[i] = (raw[i] - normalization.Means[i]) / Math.Max(normalization.StandardDeviations[i], 0.000001);

            return vector;
        }

        private static double[] ToRawVector(ImageFeatures features)
        {
            return Features.Select(feature => FeatureAccessors.GetValue(features, feature)).ToArray();
        }

        private static double Dot(IReadOnlyList<double> weights, IReadOnlyList<double> values)
        {
            var sum = 0.0;
            for (var i = 0; i < weights.Count; i++)
                sum += weights[i] * values[i];

            return sum;
        }

        private static double Sigmoid(double value)
        {
            return 1.0 / (1.0 + Math.Exp(-Math.Clamp(value, -40, 40)));
        }

        private static void PrintResult(LinearTrainingResult result)
        {
            Console.WriteLine(
                $"  {result.Model.RegionType}: threshold {result.Model.Threshold:G6}, " +
                $"recall {result.Metrics.Recall:P2}, precision {result.Metrics.Precision:P2}, " +
                $"false positives {result.Metrics.FalsePositiveRate:P2}, " +
                $"TP/FP/TN/FN {result.Metrics.TruePositive}/{result.Metrics.FalsePositive}/" +
                $"{result.Metrics.TrueNegative}/{result.Metrics.FalseNegative}");
        }

        private readonly record struct LinearSample(bool IsPositive, ImageFeatures Features);
        private readonly record struct Normalization(double[] Means, double[] StandardDeviations);
        private readonly record struct Prediction(double Probability, bool IsPositive);

        private readonly record struct LinearTrainingResult(LinearFeatureRegionModel Model, LinearMetrics Metrics)
        {
            public static LinearTrainingResult Empty(OcrRegionType regionType)
            {
                return new LinearTrainingResult(new LinearFeatureRegionModel { RegionType = regionType }, LinearMetrics.Empty);
            }
        }

        private readonly record struct LinearMetrics(
            int TruePositive,
            int FalsePositive,
            int TrueNegative,
            int FalseNegative,
            double Accuracy,
            double Recall,
            double Precision,
            double FalsePositiveRate,
            double F4Score)
        {
            public static LinearMetrics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

            public static LinearMetrics Create(int truePositive, int falsePositive, int trueNegative, int falseNegative)
            {
                var total = Math.Max(1, truePositive + falsePositive + trueNegative + falseNegative);
                var predictedPositiveTotal = truePositive + falsePositive;
                var positiveTotal = truePositive + falseNegative;
                var negativeTotal = trueNegative + falsePositive;
                var recall = positiveTotal == 0 ? 0 : (double)truePositive / positiveTotal;
                var precision = predictedPositiveTotal == 0 ? 0 : (double)truePositive / predictedPositiveTotal;

                return new LinearMetrics(
                    truePositive,
                    falsePositive,
                    trueNegative,
                    falseNegative,
                    (double)(truePositive + trueNegative) / total,
                    recall,
                    precision,
                    negativeTotal == 0 ? 0 : (double)falsePositive / negativeTotal,
                    FScore(precision, recall, beta: 4));
            }
        }

        private static double FScore(double precision, double recall, double beta)
        {
            if (precision <= 0 || recall <= 0)
                return 0;

            var betaSquared = beta * beta;
            return (1 + betaSquared) * precision * recall / ((betaSquared * precision) + recall);
        }
    }
}
