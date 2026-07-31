using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class VideoSampler
    {
        public static void PrintInfo(string videoPath)
        {
            using var capture = Open(videoPath);
            var frameCount = capture.Get(VideoCaptureProperties.FrameCount);
            var fps = capture.Get(VideoCaptureProperties.Fps);
            var width = capture.Get(VideoCaptureProperties.FrameWidth);
            var height = capture.Get(VideoCaptureProperties.FrameHeight);

            Console.WriteLine($"Video: {videoPath}");
            Console.WriteLine($"  Size: {width:0}x{height:0}");
            Console.WriteLine($"  FPS: {fps:0.###}");
            Console.WriteLine($"  Frames: {frameCount:0}");
            Console.WriteLine($"  Duration: {TimeSpan.FromSeconds(frameCount / Math.Max(1, fps))}");
        }

        public static void WriteGrid(string videoPath, string outputDir, double stepSeconds, int maxSamples)
        {
            if (stepSeconds <= 0)
                throw new ArgumentOutOfRangeException(nameof(stepSeconds), "Step seconds must be positive.");

            Directory.CreateDirectory(outputDir);

            using var capture = Open(videoPath);
            var fps = capture.Get(VideoCaptureProperties.Fps);
            var frameCount = (int)capture.Get(VideoCaptureProperties.FrameCount);
            var stepFrames = Math.Max(1, (int)Math.Round(fps * stepSeconds));
            var saved = 0;

            using var frame = new Mat();
            var thumbnails = new List<(Mat Image, string Label)>();

            for (var frameIndex = 0; frameIndex < frameCount; frameIndex += stepFrames)
            {
                if (maxSamples > 0 && saved >= maxSamples)
                    break;

                if (!ReadFrame(capture, frameIndex, frame))
                    continue;

                var timestamp = TimeSpan.FromSeconds(frameIndex / Math.Max(1, fps));
                var fileName = $"sample_{saved:D4}_frame_{frameIndex:D7}_{FormatTimestamp(timestamp)}.jpg";
                Cv2.ImWrite(Path.Combine(outputDir, fileName), frame);
                thumbnails.Add((CreateThumbnail(frame, 320, 180), $"{saved:D3} {FormatTimestamp(timestamp)}"));
                saved++;
            }

            WriteContactSheets(outputDir, "contact", thumbnails, columns: 4, rows: 5);
            foreach (var thumbnail in thumbnails)
                thumbnail.Image.Dispose();

            Console.WriteLine($"Saved {saved} grid samples to {outputDir}");
        }

        public static void WriteFrames(string videoPath, string outputDir, IEnumerable<int> frameIndices)
        {
            Directory.CreateDirectory(outputDir);

            using var capture = Open(videoPath);
            var fps = capture.Get(VideoCaptureProperties.Fps);
            using var frame = new Mat();
            var saved = 0;

            foreach (var frameIndex in frameIndices.Distinct().OrderBy(x => x))
            {
                if (!ReadFrame(capture, frameIndex, frame))
                    continue;

                var timestamp = TimeSpan.FromSeconds(frameIndex / Math.Max(1, fps));
                var fileName = $"frame_{frameIndex:D7}_{FormatTimestamp(timestamp)}.jpg";
                Cv2.ImWrite(Path.Combine(outputDir, fileName), frame);
                saved++;
            }

            Console.WriteLine($"Saved {saved} requested frames to {outputDir}");
        }

        private static VideoCapture Open(string videoPath)
        {
            var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            return capture;
        }

        private static bool ReadFrame(VideoCapture capture, int frameIndex, Mat frame)
        {
            capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
            return capture.Read(frame) && !frame.Empty();
        }

        private static string FormatTimestamp(TimeSpan timestamp)
        {
            return $"{(int)timestamp.TotalMinutes:D3}m{timestamp.Seconds:D2}s";
        }

        private static Mat CreateThumbnail(Mat frame, int width, int height)
        {
            var thumbnail = new Mat();
            Cv2.Resize(frame, thumbnail, new Size(width, height));
            return thumbnail;
        }

        private static void WriteContactSheets(
            string outputDir,
            string prefix,
            IReadOnlyList<(Mat Image, string Label)> thumbnails,
            int columns,
            int rows)
        {
            if (thumbnails.Count == 0)
                return;

            const int labelHeight = 24;
            var tileWidth = thumbnails[0].Image.Width;
            var tileHeight = thumbnails[0].Image.Height + labelHeight;
            var perSheet = columns * rows;

            for (var pageStart = 0; pageStart < thumbnails.Count; pageStart += perSheet)
            {
                using var sheet = new Mat(
                    new Size(tileWidth * columns, tileHeight * rows),
                    MatType.CV_8UC3,
                    Scalar.Black);

                for (var i = 0; i < perSheet && pageStart + i < thumbnails.Count; i++)
                {
                    var column = i % columns;
                    var row = i / columns;
                    var x = column * tileWidth;
                    var y = row * tileHeight;
                    var thumbnail = thumbnails[pageStart + i];

                    using (var imageTarget = new Mat(sheet, new Rect(x, y, tileWidth, thumbnail.Image.Height)))
                    {
                        thumbnail.Image.CopyTo(imageTarget);
                    }

                    Cv2.PutText(
                        sheet,
                        thumbnail.Label,
                        new Point(x + 8, y + thumbnail.Image.Height + 17),
                        HersheyFonts.HersheySimplex,
                        0.55,
                        Scalar.White,
                        1,
                        LineTypes.AntiAlias);
                }

                var page = pageStart / perSheet;
                Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_{page:D2}.jpg"), sheet);
            }
        }
    }
}
