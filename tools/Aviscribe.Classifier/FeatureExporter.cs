using Aviscribe.Core.Ocr;
using OpenCvSharp;
using System.Globalization;
using System.Text;

namespace Aviscribe.Classifier
{
    internal static class FeatureExporter
    {
        public static void Write(string dataRoot, string outputPath)
        {
            if (!Directory.Exists(dataRoot))
                throw new DirectoryNotFoundException($"Data root does not exist: {dataRoot}");

            EnsureParentDirectory(outputPath);

            var rows = DatasetManifest.EnumerateRows(dataRoot, includeDimensions: false).ToList();

            using var writer = new StreamWriter(outputPath, false, Encoding.UTF8);
            writer.WriteLine("relative_path,region,label,source,width,height,mean,std_dev,edge_density,bright_ratio,active_row_ratio,longest_row_run_ratio,active_column_ratio");

            var processed = 0;
            foreach (var row in rows)
            {
                var path = Path.Combine(dataRoot, row.RelativePath);
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                    continue;

                var features = ImageFeatureExtractor.Extract(image);
                writer.WriteLine(string.Join(",",
                    Csv(row.RelativePath),
                    Csv(row.Region),
                    Csv(row.Label),
                    Csv(row.Source),
                    features.Width,
                    features.Height,
                    D(features.Mean),
                    D(features.StdDev),
                    D(features.EdgeDensity),
                    D(features.BrightRatio),
                    D(features.ActiveRowRatio),
                    D(features.LongestRowRunRatio),
                    D(features.ActiveColumnRatio)));

                processed++;
                if (processed % 1000 == 0)
                    Console.WriteLine($"Processed {processed}/{rows.Count}");
            }

            Console.WriteLine($"Wrote {processed} rows to {outputPath}");
        }

        private static string D(double value)
        {
            return value.ToString("G17", CultureInfo.InvariantCulture);
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
}
