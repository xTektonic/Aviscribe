using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class MoonGetInspector
    {
        private static readonly Rect MoonGetDetectionBounds = new(320, 600, 1250, 250);

        public static void Print(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                {
                    Console.WriteLine($"{path}: could not read image");
                    continue;
                }

                PrintImage(Path.GetFileName(path), image);
            }
        }

        public static void PrintVideoFrames(string videoPath, IEnumerable<int> frames)
        {
            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            using var frame = new Mat();
            foreach (var frameIndex in frames)
            {
                capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
                if (!capture.Read(frame) || frame.Empty())
                {
                    Console.WriteLine($"frame {frameIndex}: could not read");
                    continue;
                }

                using var crop = new Mat(frame, MoonGetDetectionBounds);
                PrintImage($"frame {frameIndex}", crop);
            }
        }

        private static void PrintImage(string label, Mat image)
        {
            var metrics = MoonGetExperiment.Measure(image);
            var separator = TextDetection.AnalyzeMoonGetSeparatorLayout(image);
            Console.WriteLine(label);
            Console.WriteLine($"  detected: {TextDetection.HasMoonText(image)}");
            Console.WriteLine(
                $"  pix {metrics.WhitePixels}, ratio {metrics.WhiteRatio:0.000}, band {metrics.BandScore:0}, " +
                $"rows {metrics.BandRows}, center {metrics.BandCenterRatio:0.00}, cols {metrics.ActiveColumns}, span {metrics.SpanWidth}");
            Console.WriteLine(
                $"  comps {metrics.TextComponentCount}, compArea {metrics.TextComponentArea}, compSpan {metrics.TextComponentSpan}, " +
                $"outline {metrics.OutlinedPixels}, outlineCols {metrics.OutlinedColumns}");
            Console.WriteLine(
                $"  separator {separator.Detected}, line {separator.LineStart}-{separator.LineEnd} rows {separator.LineRows}, " +
                $"avg {separator.LineAverage:0.0}/{separator.SurroundingAverage:0.0}, title {separator.TitleRows}/{separator.StrongTitleRows}, " +
                $"banner {separator.StrongBannerRows}/{separator.FadedBannerRows}, panel {separator.TopPanelRows}, " +
                $"scenery {separator.SceneryTitleRows}, span {separator.TitleColumnSpan}, nameRows {separator.NameRows}");
        }
    }
}
