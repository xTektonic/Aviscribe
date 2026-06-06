using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class FrameExtractor
    {
        public static void Extract(string videoPath, string outputDir, int modulo, IReadOnlyList<(string Name, Rect Bounds)>? regions)
        {
            if (modulo <= 0)
                throw new ArgumentOutOfRangeException(nameof(modulo), "Modulo must be positive.");

            Directory.CreateDirectory(outputDir);

            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            var totalFrames = capture.FrameCount;
            var frameIndex = 0;
            var savedIndex = 0;

            using var frame = new Mat();

            while (capture.Read(frame) && !frame.Empty())
            {
                frameIndex++;

                if (frameIndex % modulo != 0)
                    continue;

                if (regions == null)
                {
                    WriteImage(frame, outputDir, $"img_{savedIndex:D6}.jpg");
                }
                else
                {
                    foreach (var region in regions)
                    {
                        Directory.CreateDirectory(Path.Combine(outputDir, region.Name));
                        using var cropped = new Mat(frame, region.Bounds);
                        WriteImage(cropped, Path.Combine(outputDir, region.Name), $"img_{savedIndex:D6}.jpg");
                    }
                }

                if (savedIndex % 50 == 0)
                    Console.WriteLine($"Processed frame {frameIndex}/{totalFrames}, saved {savedIndex}.");

                savedIndex++;
            }

            Console.WriteLine($"Done. Saved {savedIndex} sampled frames.");
        }

        private static void WriteImage(Mat image, string outputDir, string fileName)
        {
            var path = Path.Combine(outputDir, fileName);
            Cv2.ImWrite(path, image, new ImageEncodingParam(ImwriteFlags.JpegQuality, 90));
        }
    }
}
