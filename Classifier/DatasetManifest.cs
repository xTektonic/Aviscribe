using OpenCvSharp;
using System.Globalization;
using System.Text;

namespace Aviscribe.Classifier
{
    internal static class DatasetManifest
    {
        public static void Write(string dataRoot, string outputPath)
        {
            EnsureDataRoot(dataRoot);
            EnsureParentDirectory(outputPath);

            var rows = EnumerateRows(dataRoot, includeDimensions: true).ToList();

            using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
            writer.WriteLine("relative_path,region,label,width,height,source");

            foreach (var row in rows)
            {
                writer.WriteLine(string.Join(",",
                    Csv(row.RelativePath),
                    Csv(row.Region),
                    Csv(row.Label),
                    row.Width.ToString(CultureInfo.InvariantCulture),
                    row.Height.ToString(CultureInfo.InvariantCulture),
                    Csv(row.Source)));
            }

            Console.WriteLine($"Wrote {rows.Count} rows to {outputPath}");
        }

        public static IEnumerable<DatasetRow> EnumerateRows(string dataRoot, bool includeDimensions = true)
        {
            foreach (var region in new[] { "Talkatoo", "MoonGet" })
            {
                foreach (var label in new[] { "Good", "Bad" })
                {
                    var dir = Path.Combine(dataRoot, "ClassifiedData", region, label);
                    foreach (var path in EnumerateImages(dir))
                        yield return CreateRow(dataRoot, path, region, label.ToLowerInvariant(), "classified", includeDimensions);
                }
            }

            foreach (var region in new[] { "Talkatoo", "MoonGet" })
            {
                var dir = Path.Combine(dataRoot, "TestData", region);
                foreach (var path in EnumerateImages(dir))
                    yield return CreateRow(dataRoot, path, region, "unknown", "test", includeDimensions);
            }

            foreach (var path in EnumerateImages(Path.Combine(dataRoot, "StoryMoons")))
                yield return CreateRow(dataRoot, path, "StoryMoon", "good", "story-moon-curated", includeDimensions);

            foreach (var path in EnumerateImages(Path.Combine(dataRoot, "StoryMoonData")))
                yield return CreateRow(dataRoot, path, "StoryMoon", "unknown", "story-moon-sampled", includeDimensions);

        }

        private static IEnumerable<string> EnumerateImages(string dir)
        {
            if (!Directory.Exists(dir))
                return [];

            return Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(DatasetInspector.IsImage);
        }

        private static DatasetRow CreateRow(
            string dataRoot,
            string path,
            string region,
            string label,
            string source,
            bool includeDimensions)
        {
            var relative = Path.GetRelativePath(dataRoot, path);

            if (!includeDimensions)
                return new DatasetRow(relative, region, label, 0, 0, source);

            using var image = Cv2.ImRead(path);
            return new DatasetRow(relative, region, label, image.Width, image.Height, source);
        }

        private static void EnsureDataRoot(string dataRoot)
        {
            if (!Directory.Exists(dataRoot))
                throw new DirectoryNotFoundException($"Data root does not exist: {dataRoot}");
        }

        private static void EnsureParentDirectory(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);
        }

        private static string Csv(string value)
        {
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }

    internal readonly record struct DatasetRow(
        string RelativePath,
        string Region,
        string Label,
        int Width,
        int Height,
        string Source);
}
