namespace Aviscribe.Classifier
{
    internal static class DatasetPaths
    {
        public static readonly string DefaultDataRoot =
            Path.Combine("tools", "Aviscribe.Classifier", "Data");
        public static readonly string DefaultManifestPath =
            Path.Combine("tools", "Aviscribe.Classifier", "Output", "dataset-manifest.csv");
        public static readonly string DefaultFeaturesPath =
            Path.Combine("tools", "Aviscribe.Classifier", "Output", "features.csv");
    }
}
