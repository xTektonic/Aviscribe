using Aviscribe.Core.Ocr;

namespace Aviscribe.Classifier
{
    internal static class ThresholdExperiment
    {
        public static void PrintSummary(string dataRoot, double minimumRecallTarget)
        {
            if (!Directory.Exists(dataRoot))
                throw new DirectoryNotFoundException($"Data root does not exist: {dataRoot}");

            Console.WriteLine($"High-recall threshold search (target recall: {minimumRecallTarget:P2})");

            foreach (var result in ThresholdRuleSearch.SearchRuleSets(dataRoot, minimumRecallTarget, Console.WriteLine))
            {
                if (result.Rules.Count == 0)
                {
                    Console.WriteLine($"  {result.RegionType}: no classified samples");
                    continue;
                }

                Console.WriteLine($"  {result.RegionType}: {FormatRules(result.Rules)}");
                Console.WriteLine(
                    $"    accuracy {result.Accuracy:P2}, recall {result.Recall:P2}, " +
                    $"precision {result.Precision:P2}, false positives {result.FalsePositiveRate:P2}, " +
                    $"TP/FP/TN/FN {result.TruePositive}/{result.FalsePositive}/{result.TrueNegative}/{result.FalseNegative}");
            }
        }

        private static string FormatRules(IReadOnlyList<FeatureThresholdRule> rules)
        {
            return string.Join(" AND ", rules.Select(rule =>
                $"{rule.Feature} {(rule.PositiveWhenGreater ? ">=" : "<")} {rule.Threshold:G6}"));
        }
    }

    internal readonly record struct FeatureAccessor(string Name, ImageFeatureName CoreName, Func<ImageFeatures, double> GetValue);

    internal static class FeatureAccessors
    {
        public static readonly FeatureAccessor[] All =
        [
            new("mean", ImageFeatureName.Mean, f => f.Mean),
            new("std_dev", ImageFeatureName.StdDev, f => f.StdDev),
            new("edge_density", ImageFeatureName.EdgeDensity, f => f.EdgeDensity),
            new("bright_ratio", ImageFeatureName.BrightRatio, f => f.BrightRatio),
            new("active_row_ratio", ImageFeatureName.ActiveRowRatio, f => f.ActiveRowRatio),
            new("longest_row_run_ratio", ImageFeatureName.LongestRowRunRatio, f => f.LongestRowRunRatio),
            new("active_column_ratio", ImageFeatureName.ActiveColumnRatio, f => f.ActiveColumnRatio)
        ];

        public static double GetValue(ImageFeatures features, ImageFeatureName feature)
        {
            return All.First(accessor => accessor.CoreName == feature).GetValue(features);
        }
    }
}
