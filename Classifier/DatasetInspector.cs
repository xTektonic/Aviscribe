using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class DatasetInspector
    {
        public static void PrintSummary(string dataRoot)
        {
            Console.WriteLine("Dataset");
            PrintDirectoryCount(dataRoot, "UnclassifiedData", recursive: true);
            PrintDirectoryCount(dataRoot, "ClassifiedData", recursive: true);
            PrintDirectoryCount(dataRoot, "TestData", recursive: true);
            PrintDirectoryCount(dataRoot, "StoryMoonData", recursive: false);
            PrintDirectoryCount(dataRoot, "StoryMoons", recursive: false);

            Console.WriteLine();
            Console.WriteLine("Labels");
            PrintLabelCount(dataRoot, "ClassifiedData", "Talkatoo", "Good");
            PrintLabelCount(dataRoot, "ClassifiedData", "Talkatoo", "Bad");
            PrintLabelCount(dataRoot, "ClassifiedData", "MoonGet", "Good");
            PrintLabelCount(dataRoot, "ClassifiedData", "MoonGet", "Bad");

            Console.WriteLine();
            Console.WriteLine("Sample dimensions");
            PrintFirstImageSize(dataRoot, "ClassifiedData", "Talkatoo", "Good");
            PrintFirstImageSize(dataRoot, "ClassifiedData", "MoonGet", "Good");
            PrintFirstImageSize(dataRoot, "StoryMoons");
        }

        private static void PrintDirectoryCount(string dataRoot, string relativePath, bool recursive)
        {
            var path = Path.Combine(dataRoot, relativePath);
            var count = CountImages(path, recursive);
            Console.WriteLine($"  {relativePath}: {count}");
        }

        private static void PrintLabelCount(string dataRoot, params string[] parts)
        {
            var relativePath = Path.Combine(parts);
            var path = Path.Combine(dataRoot, relativePath);
            var count = CountImages(path, recursive: false);
            Console.WriteLine($"  {relativePath}: {count}");
        }

        private static int CountImages(string path, bool recursive)
        {
            if (!Directory.Exists(path))
                return 0;

            var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
            return Directory.EnumerateFiles(path, "*.*", option)
                .Count(IsImage);
        }

        private static void PrintFirstImageSize(string dataRoot, params string[] parts)
        {
            var relativePath = Path.Combine(parts);
            var path = Path.Combine(dataRoot, relativePath);
            var sample = Directory.Exists(path)
                ? Directory.EnumerateFiles(path, "*.*", SearchOption.TopDirectoryOnly).FirstOrDefault(IsImage)
                : null;

            if (sample == null)
            {
                Console.WriteLine($"  {relativePath}: no images");
                return;
            }

            using var image = Cv2.ImRead(sample);
            Console.WriteLine($"  {relativePath}: {image.Width}x{image.Height}");
        }

        internal static bool IsImage(string path)
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ||
                   ext.Equals(".png", StringComparison.OrdinalIgnoreCase);
        }
    }
}
