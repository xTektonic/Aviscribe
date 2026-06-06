using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class ThresholdRuleSearch
    {
        public static IReadOnlyList<ThresholdSearchResult> SearchAll(
            string dataRoot,
            double minimumRecallTarget,
            Action<string>? progress = null)
        {
            return new[]
            {
                SearchRegion(dataRoot, OcrRegionType.Talkatoo, minimumRecallTarget, progress),
                SearchRegion(dataRoot, OcrRegionType.MoonGet, minimumRecallTarget, progress)
            };
        }

        public static IReadOnlyList<ThresholdRuleSetCandidate> SearchRuleSets(
            string dataRoot,
            double minimumRecallTarget,
            Action<string>? progress = null)
        {
            return new[]
            {
                SearchRuleSet(dataRoot, OcrRegionType.Talkatoo, minimumRecallTarget, progress),
                SearchRuleSet(dataRoot, OcrRegionType.MoonGet, minimumRecallTarget, progress)
            };
        }

        public static ThresholdRuleSetCandidate SearchRuleSet(
            string dataRoot,
            OcrRegionType regionType,
            double minimumRecallTarget,
            Action<string>? progress = null)
        {
            var region = regionType.ToString();
            var rows = DatasetManifest.EnumerateRows(dataRoot, includeDimensions: false)
                .Where(r => r.Region == region && (r.Label == "good" || r.Label == "bad"))
                .ToList();

            var samples = LoadSamples(dataRoot, rows, region, progress);
            if (samples.Count == 0)
                return ThresholdRuleSetCandidate.Empty(regionType);

            var singleRules = new List<ThresholdSearchResult>();
            foreach (var feature in FeatureAccessors.All)
                singleRules.AddRange(SearchFeature(regionType, samples, feature));

            var bestSingle = SelectBest(singleRules, minimumRecallTarget);
            var best = ThresholdRuleSetCandidate.FromSingle(bestSingle);

            var pairInputs = singleRules
                .Where(rule => rule.Recall >= minimumRecallTarget)
                .OrderBy(rule => rule.FalsePositiveRate)
                .ThenByDescending(rule => rule.Precision)
                .Take(80)
                .ToList();

            for (var i = 0; i < pairInputs.Count; i++)
            {
                for (var j = i + 1; j < pairInputs.Count; j++)
                {
                    var candidate = ScoreRuleSet(regionType, samples, [pairInputs[i].ToRule(), pairInputs[j].ToRule()]);
                    if (IsBetterRuleSet(candidate, best, minimumRecallTarget))
                        best = candidate;
                }
            }

            return best;
        }

        public static ThresholdSearchResult SearchRegion(
            string dataRoot,
            OcrRegionType regionType,
            double minimumRecallTarget,
            Action<string>? progress = null)
        {
            var region = regionType.ToString();
            var rows = DatasetManifest.EnumerateRows(dataRoot, includeDimensions: false)
                .Where(r => r.Region == region && (r.Label == "good" || r.Label == "bad"))
                .ToList();

            var samples = LoadSamples(dataRoot, rows, region, progress);
            if (samples.Count == 0)
                return ThresholdSearchResult.Empty(regionType);

            var candidates = new List<ThresholdSearchResult>();
            foreach (var feature in FeatureAccessors.All)
                candidates.AddRange(SearchFeature(regionType, samples, feature));

            return SelectBest(candidates, minimumRecallTarget);
        }

        public static List<ThresholdSearchResult> SearchFeature(
            OcrRegionType regionType,
            IReadOnlyList<ThresholdSample> samples,
            FeatureAccessor feature)
        {
            var ordered = samples
                .Select(s => new FeatureSample(feature.GetValue(s.Features), s.IsPositive))
                .OrderBy(s => s.Value)
                .ToList();

            var total = ordered.Count;
            var totalPositive = ordered.Count(s => s.IsPositive);
            var totalNegative = total - totalPositive;

            var results = new List<ThresholdSearchResult>();
            var prefixPositive = 0;
            var prefixNegative = 0;
            var index = 0;

            while (index < ordered.Count)
            {
                var threshold = ordered[index].Value;
                var equalPositive = 0;
                var equalNegative = 0;

                while (index < ordered.Count && ordered[index].Value.Equals(threshold))
                {
                    if (ordered[index].IsPositive)
                        equalPositive++;
                    else
                        equalNegative++;

                    index++;
                }

                results.Add(Score(
                    regionType,
                    feature,
                    threshold,
                    positiveWhenGreater: true,
                    truePositive: totalPositive - prefixPositive,
                    falsePositive: totalNegative - prefixNegative,
                    trueNegative: prefixNegative,
                    falseNegative: prefixPositive));

                results.Add(Score(
                    regionType,
                    feature,
                    threshold,
                    positiveWhenGreater: false,
                    truePositive: prefixPositive,
                    falsePositive: prefixNegative,
                    trueNegative: totalNegative - prefixNegative,
                    falseNegative: totalPositive - prefixPositive));

                prefixPositive += equalPositive;
                prefixNegative += equalNegative;
            }

            return results;
        }

        private static List<ThresholdSample> LoadSamples(
            string dataRoot,
            IReadOnlyList<DatasetRow> rows,
            string region,
            Action<string>? progress)
        {
            var samples = new ThresholdSample?[rows.Count];
            var processed = 0;

            Parallel.For(
                0,
                rows.Count,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount - 1)
                },
                i =>
                {
                    var path = Path.Combine(dataRoot, rows[i].RelativePath);
                    using var image = Cv2.ImRead(path);
                    if (!image.Empty())
                        samples[i] = new ThresholdSample(rows[i].Label == "good", ImageFeatureExtractor.Extract(image));

                    var done = Interlocked.Increment(ref processed);
                    if (done % 1000 == 0)
                        progress?.Invoke($"  {region}: loaded {done}/{rows.Count}");
                });

            var loaded = new List<ThresholdSample>(rows.Count);
            foreach (var sample in samples)
            {
                if (sample.HasValue)
                    loaded.Add(sample.Value);
            }

            return loaded;
        }

        private static ThresholdSearchResult SelectBest(
            IReadOnlyList<ThresholdSearchResult> candidates,
            double minimumRecallTarget)
        {
            var eligible = candidates
                .Where(c => c.Recall >= minimumRecallTarget)
                .OrderBy(c => c.FalsePositiveRate)
                .ThenByDescending(c => c.Precision)
                .ThenByDescending(c => c.F4Score)
                .ToList();

            if (eligible.Count > 0)
                return eligible[0];

            return candidates
                .OrderByDescending(c => c.F4Score)
                .ThenByDescending(c => c.Recall)
                .ThenBy(c => c.FalsePositiveRate)
                .FirstOrDefault(ThresholdSearchResult.Empty(default));
        }

        private static ThresholdRuleSetCandidate ScoreRuleSet(
            OcrRegionType regionType,
            IReadOnlyList<ThresholdSample> samples,
            IReadOnlyList<FeatureThresholdRule> rules)
        {
            var truePositive = 0;
            var falsePositive = 0;
            var trueNegative = 0;
            var falseNegative = 0;

            foreach (var sample in samples)
            {
                var predictedPositive = MatchesAll(sample.Features, rules);

                if (sample.IsPositive && predictedPositive)
                    truePositive++;
                else if (!sample.IsPositive && predictedPositive)
                    falsePositive++;
                else if (!sample.IsPositive)
                    trueNegative++;
                else
                    falseNegative++;
            }

            return ThresholdRuleSetCandidate.Create(regionType, rules, truePositive, falsePositive, trueNegative, falseNegative);
        }

        private static bool MatchesAll(ImageFeatures features, IReadOnlyList<FeatureThresholdRule> rules)
        {
            foreach (var rule in rules)
            {
                var value = FeatureAccessors.GetValue(features, rule.Feature);
                var matches = rule.PositiveWhenGreater
                    ? value >= rule.Threshold
                    : value < rule.Threshold;

                if (!matches)
                    return false;
            }

            return true;
        }

        private static bool IsBetterRuleSet(
            ThresholdRuleSetCandidate candidate,
            ThresholdRuleSetCandidate current,
            double minimumRecallTarget)
        {
            var candidateEligible = candidate.Recall >= minimumRecallTarget;
            var currentEligible = current.Recall >= minimumRecallTarget;

            if (candidateEligible && !currentEligible)
                return true;

            if (!candidateEligible && currentEligible)
                return false;

            if (candidateEligible && currentEligible)
            {
                if (candidate.FalsePositiveRate < current.FalsePositiveRate)
                    return true;

                if (Math.Abs(candidate.FalsePositiveRate - current.FalsePositiveRate) < 0.0000001 &&
                    candidate.Precision > current.Precision)
                    return true;

                return false;
            }

            return candidate.F4Score > current.F4Score;
        }

        private static ThresholdSearchResult Score(
            OcrRegionType regionType,
            FeatureAccessor feature,
            double threshold,
            bool positiveWhenGreater,
            int truePositive,
            int falsePositive,
            int trueNegative,
            int falseNegative)
        {
            var total = Math.Max(1, truePositive + falsePositive + trueNegative + falseNegative);
            var predictedPositiveTotal = truePositive + falsePositive;
            var positiveTotal = truePositive + falseNegative;
            var negativeTotal = trueNegative + falsePositive;
            var recall = positiveTotal == 0 ? 0 : (double)truePositive / positiveTotal;
            var precision = predictedPositiveTotal == 0 ? 0 : (double)truePositive / predictedPositiveTotal;

            return new ThresholdSearchResult(
                regionType,
                feature.Name,
                feature.CoreName,
                threshold,
                positiveWhenGreater,
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

        private static double FScore(double precision, double recall, double beta)
        {
            if (precision <= 0 || recall <= 0)
                return 0;

            var betaSquared = beta * beta;
            return (1 + betaSquared) * precision * recall / ((betaSquared * precision) + recall);
        }

        private readonly record struct FeatureSample(double Value, bool IsPositive);
    }

    internal readonly record struct ThresholdSample(bool IsPositive, ImageFeatures Features);

    internal readonly record struct ThresholdSearchResult(
        OcrRegionType RegionType,
        string FeatureName,
        ImageFeatureName CoreFeatureName,
        double Threshold,
        bool PositiveWhenGreater,
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
        public FeatureThresholdRule ToRule()
        {
            return new FeatureThresholdRule(RegionType, CoreFeatureName, Threshold, PositiveWhenGreater);
        }

        public static ThresholdSearchResult Empty(OcrRegionType regionType)
        {
            return new ThresholdSearchResult(regionType, string.Empty, default, 0, true, 0, 0, 0, 0, 0, 0, 0, 0, 0);
        }
    }

    internal readonly record struct ThresholdRuleSetCandidate(
        OcrRegionType RegionType,
        IReadOnlyList<FeatureThresholdRule> Rules,
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
        public static ThresholdRuleSetCandidate FromSingle(ThresholdSearchResult result)
        {
            if (string.IsNullOrWhiteSpace(result.FeatureName))
                return Empty(result.RegionType);

            return Create(
                result.RegionType,
                [result.ToRule()],
                result.TruePositive,
                result.FalsePositive,
                result.TrueNegative,
                result.FalseNegative);
        }

        public static ThresholdRuleSetCandidate Create(
            OcrRegionType regionType,
            IReadOnlyList<FeatureThresholdRule> rules,
            int truePositive,
            int falsePositive,
            int trueNegative,
            int falseNegative)
        {
            var total = Math.Max(1, truePositive + falsePositive + trueNegative + falseNegative);
            var predictedPositiveTotal = truePositive + falsePositive;
            var positiveTotal = truePositive + falseNegative;
            var negativeTotal = trueNegative + falsePositive;
            var recall = positiveTotal == 0 ? 0 : (double)truePositive / positiveTotal;
            var precision = predictedPositiveTotal == 0 ? 0 : (double)truePositive / predictedPositiveTotal;

            return new ThresholdRuleSetCandidate(
                regionType,
                rules.ToList(),
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

        public static ThresholdRuleSetCandidate Empty(OcrRegionType regionType)
        {
            return new ThresholdRuleSetCandidate(regionType, [], 0, 0, 0, 0, 0, 0, 0, 0, 0);
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
