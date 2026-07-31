namespace Aviscribe.Classifier
{
    internal static class DatasetPaths
    {
        public const string DefaultDataRoot = @"C:\Users\amaho\Desktop\AviscribeClassifierData";
        public static readonly string DefaultManifestPath =
            Path.Combine("tools", "Aviscribe.Classifier", "Output", "dataset-manifest.csv");
        public static readonly string DefaultFeaturesPath =
            Path.Combine("tools", "Aviscribe.Classifier", "Output", "features.csv");
    }
}
