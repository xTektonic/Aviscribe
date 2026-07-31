namespace Aviscribe.Classifier
{
    internal static class DatasetPaths
    {
        public const string DefaultDataRoot = @"[removed]";
        public static readonly string DefaultManifestPath =
            Path.Combine("tools", "Aviscribe.Classifier", "Output", "dataset-manifest.csv");
        public static readonly string DefaultFeaturesPath =
            Path.Combine("tools", "Aviscribe.Classifier", "Output", "features.csv");
    }
}
