using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aviscribe.Core.Ocr
{
    public class FeatureThresholdRuleSet
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string Name { get; set; } = "feature-thresholds";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public double MinimumRecallTarget { get; set; }
        public double MaximumFalsePositiveRate { get; set; }
        public List<FeatureThresholdRule> Rules { get; set; } = new();

        public static FeatureThresholdRuleSet Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<FeatureThresholdRuleSet>(json, JsonOptions)
                ?? new FeatureThresholdRuleSet();
        }

        public void Save(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }

        public ITextPresenceDetector CreateDetector()
        {
            return new FeatureThresholdTextPresenceDetector(Rules);
        }
    }
}
