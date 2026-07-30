using Aviscribe.Core.Ocr;

namespace Aviscribe.Classifier
{
    internal static class ThresholdRuleExporter
    {
        public static void Write(
            string dataRoot,
            string outputPath,
            double minimumRecallTarget,
            double maximumFalsePositiveRate)
        {
            if (!Directory.Exists(dataRoot))
                throw new DirectoryNotFoundException($"Data root does not exist: {dataRoot}");

            var results = ThresholdRuleSearch.SearchRuleSets(dataRoot, minimumRecallTarget, Console.WriteLine);
            var ruleSet = new FeatureThresholdRuleSet
            {
                Name = "Aviscribe feature-threshold detector rules",
                CreatedUtc = DateTime.UtcNow,
                MinimumRecallTarget = minimumRecallTarget,
                MaximumFalsePositiveRate = maximumFalsePositiveRate,
                Rules = results
                    .Where(result =>
                        result.Rules.Count > 0 &&
                        result.Recall >= minimumRecallTarget &&
                        result.FalsePositiveRate <= maximumFalsePositiveRate)
                    .SelectMany(result => result.Rules)
                    .ToList()
            };

            ruleSet.Save(outputPath);

            Console.WriteLine($"Wrote {ruleSet.Rules.Count} accepted rules to {outputPath}");
            foreach (var result in results)
            {
                var accepted = ruleSet.Rules.Any(rule => rule.RegionType == result.RegionType);
                Console.WriteLine(
                    $"  {(accepted ? "ACCEPT" : "SKIP")} {result.RegionType}: {FormatRules(result.Rules)}, " +
                    $"recall {result.Recall:P2}, precision {result.Precision:P2}, " +
                    $"false positives {result.FalsePositiveRate:P2}");
            }
        }

        private static string FormatRules(IReadOnlyList<FeatureThresholdRule> rules)
        {
            return rules.Count == 0
                ? "no rule"
                : string.Join(" AND ", rules.Select(rule =>
                    $"{rule.Feature} {(rule.PositiveWhenGreater ? ">=" : "<")} {rule.Threshold:G6}"));
        }
    }
}
