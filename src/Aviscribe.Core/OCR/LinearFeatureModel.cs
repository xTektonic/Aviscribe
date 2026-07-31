using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aviscribe.Core.Ocr
{
    public class LinearFeatureModel
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public string Name { get; set; } = "linear-feature-detector";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public double MinimumRecallTarget { get; set; }
        public double MaximumFalsePositiveRate { get; set; }
        public List<LinearFeatureRegionModel> Regions { get; set; } = new();

        public static LinearFeatureModel Load(string path)
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<LinearFeatureModel>(json, JsonOptions)
                ?? new LinearFeatureModel();
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
            return new LinearFeatureTextPresenceDetector(this);
        }
    }

    public class LinearFeatureRegionModel
    {
        public OcrRegionType RegionType { get; set; }
        public List<ImageFeatureName> Features { get; set; } = new();
        public List<double> Means { get; set; } = new();
        public List<double> StandardDeviations { get; set; } = new();
        public List<double> Weights { get; set; } = new();
        public double Bias { get; set; }
        public double Threshold { get; set; } = 0.5;
    }
}
