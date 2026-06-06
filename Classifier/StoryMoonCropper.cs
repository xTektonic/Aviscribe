using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class StoryMoonCropper
    {
        private static readonly Rect ReferenceCrop = new(480, 700, 960, 300);
        private const int ReferenceWidth = 1920;
        private const int ReferenceHeight = 1080;

        public static void Write(string dataRoot, string outputDir)
        {
            if (!Directory.Exists(dataRoot))
                throw new DirectoryNotFoundException($"Data root does not exist: {dataRoot}");

            var written = 0;
            written += WriteSet(
                Path.Combine(dataRoot, "StoryMoons"),
                Path.Combine(outputDir, "Good"),
                "good");
            written += WriteSet(
                Path.Combine(dataRoot, "StoryMoonData"),
                Path.Combine(outputDir, "Unknown"),
                "unknown");

            Console.WriteLine($"Wrote {written} StoryMoon crops to {outputDir}");
        }

        private static int WriteSet(string inputDir, string outputDir, string prefix)
        {
            if (!Directory.Exists(inputDir))
                return 0;

            Directory.CreateDirectory(outputDir);

            var count = 0;
            foreach (var path in Directory.EnumerateFiles(inputDir, "*.*", SearchOption.TopDirectoryOnly).Where(DatasetInspector.IsImage))
            {
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                    continue;

                var crop = ScaleCrop(image.Width, image.Height);
                using var cropped = new Mat(image, crop);

                var fileName = $"{prefix}_{Path.GetFileNameWithoutExtension(path)}.jpg";
                var outputPath = Path.Combine(outputDir, fileName);
                Cv2.ImWrite(outputPath, cropped, new ImageEncodingParam(ImwriteFlags.JpegQuality, 92));

                count++;
                if (count % 100 == 0)
                    Console.WriteLine($"  {prefix}: {count}");
            }

            return count;
        }

        private static Rect ScaleCrop(int width, int height)
        {
            var xScale = width / (double)ReferenceWidth;
            var yScale = height / (double)ReferenceHeight;

            var x = (int)Math.Round(ReferenceCrop.X * xScale);
            var y = (int)Math.Round(ReferenceCrop.Y * yScale);
            var w = (int)Math.Round(ReferenceCrop.Width * xScale);
            var h = (int)Math.Round(ReferenceCrop.Height * yScale);

            x = Math.Clamp(x, 0, Math.Max(0, width - 1));
            y = Math.Clamp(y, 0, Math.Max(0, height - 1));
            w = Math.Clamp(w, 1, width - x);
            h = Math.Clamp(h, 1, height - y);

            return new Rect(x, y, w, h);
        }
    }
}
