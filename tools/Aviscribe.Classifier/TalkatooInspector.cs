using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class TalkatooInspector
    {
        private static readonly Rect TalkatooBounds = new(600, 862, 715, 48);

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

                using var crop = new Mat(frame, TalkatooBounds);
                PrintImage($"frame {frameIndex}", crop);
            }
        }

        private static void PrintImage(string label, Mat image)
        {
            var metrics = Measure(image);
            var gate = TalkatooStaticGate.Analyze(image);
            Console.WriteLine(label);
            Console.WriteLine($"  detected: {TextDetection.HasTalkatooText(image)}");
            Console.WriteLine(
                $"  static gate: marker {gate.MarkerBounds}, text {gate.TextBounds}, " +
                $"band yellow {gate.YellowPixels}, active columns {gate.ActiveColumns}, " +
                $"occupancy {gate.Occupancy:P2}, total yellow {gate.TotalYellowPixels}");
            Console.WriteLine($"  yellow pixels: {metrics.YellowPixels}, ratio {metrics.YellowRatio:P2}");
            Console.WriteLine($"  band: y={metrics.BandTop}..{metrics.BandBottom}, rows {metrics.BandRows}, score {metrics.BandScore:0.00}");
            Console.WriteLine($"  columns: active {metrics.ActiveColumns}, longest run {metrics.LongestColumnRun}, span {metrics.Left}..{metrics.Right}, width {metrics.SpanWidth}");
            Console.WriteLine($"  row peak/median active: {metrics.PeakRow}/{metrics.MedianActiveRow:0.0}, fragmented columns {metrics.FragmentedColumns}");
            PrintIconComponents(image);
            PrintWhiteMarkerComponents(image);
            PrintWideWhiteOverlay(image);
        }

        private static void PrintWideWhiteOverlay(Mat image)
        {
            var width = image.Width;
            var height = image.Height;
            var whitePixels = 0;
            var activeColumns = 0;
            for (var x = 80; x < width; x++)
            {
                var columnWhitePixels = 0;
                for (var y = 0; y < height; y++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));

                    if (max >= 205 && min >= 165 && max - min <= 70)
                    {
                        whitePixels++;
                        columnWhitePixels++;
                    }
                }

                if (columnWhitePixels >= 2)
                    activeColumns++;
            }

            Console.WriteLine($"  wide white: pixels {whitePixels}, active columns {activeColumns}");
        }

        private static void PrintWhiteMarkerComponents(Mat image)
        {
            var rows = image.Rows;
            var iconSearchWidth = Math.Min(image.Width, 92);
            using var whiteMask = new Mat(new Size(iconSearchWidth, image.Height), MatType.CV_8UC1, Scalar.Black);
            var whitePixels = 0;

            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < iconSearchWidth; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));

                    if (max >= 205 && min >= 165 && max - min <= 70)
                    {
                        whiteMask.Set(y, x, 255);
                        whitePixels++;
                    }
                }
            }

            Console.WriteLine($"  white marker pixels: {whitePixels}");
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var componentCount = Cv2.ConnectedComponentsWithStats(whiteMask, labels, stats, centroids);
            for (var i = 1; i < componentCount; i++)
            {
                var x = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                var y = stats.Get<int>(i, (int)ConnectedComponentsTypes.Top);
                var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                var area = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);
                Console.WriteLine($"    white x {x}, y {y}, w {width}, h {height}, area {area}");
            }
        }

        private static void PrintIconComponents(Mat image)
        {
            var rows = image.Rows;
            var iconSearchWidth = Math.Min(image.Width, 88);
            using var iconMask = new Mat(new Size(iconSearchWidth, image.Height), MatType.CV_8UC1, Scalar.Black);

            for (var y = 0; y < rows; y++)
            {
                for (var x = 0; x < iconSearchWidth; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));

                    if ((max >= 165 && min >= 105) || (max >= 120 && max - min >= 45))
                        iconMask.Set(y, x, 255);
                }
            }

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            Cv2.MorphologyEx(iconMask, iconMask, MorphTypes.Close, kernel);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var componentCount = Cv2.ConnectedComponentsWithStats(iconMask, labels, stats, centroids);

            Console.WriteLine("  icon components:");
            for (var i = 1; i < componentCount; i++)
            {
                var x = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                var y = stats.Get<int>(i, (int)ConnectedComponentsTypes.Top);
                var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                var area = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);
                var fill = area / (double)Math.Max(1, width * height);
                Console.WriteLine($"    x {x}, y {y}, w {width}, h {height}, area {area}, fill {fill:0.00}");
            }
        }

        private static Metrics Measure(Mat image)
        {
            using var mask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.Black);
            var imageRows = image.Rows;
            var imageCols = image.Cols;

            for (var y = 0; y < imageRows; y++)
            {
                for (var x = 0; x < imageCols; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    if (x >= 80 && IsYellowTextPixel(r, g, b))
                        mask.Set(y, x, 255);
                }
            }

            var maskWidth = mask.Width;
            var maskHeight = mask.Height;
            var rowCounts = new int[maskHeight];
            var yellowPixels = 0;
            for (var y = 0; y < maskHeight; y++)
            {
                var count = 0;
                for (var x = 0; x < maskWidth; x++)
                {
                    if (mask.At<byte>(y, x) > 0)
                        count++;
                }

                rowCounts[y] = count;
                yellowPixels += count;
            }

            var threshold = Math.Max(22, maskWidth * 0.045);
            var bestStart = 0;
            var bestEnd = 0;
            var bestScore = 0.0;
            for (var start = 0; start < maskHeight; start++)
            {
                var score = 0.0;
                for (var end = start; end < Math.Min(maskHeight, start + 34); end++)
                {
                    score += Math.Max(0, rowCounts[end] - threshold);
                    if (end - start + 1 >= 8 && score > bestScore)
                    {
                        bestScore = score;
                        bestStart = start;
                        bestEnd = end + 1;
                    }
                }
            }

            var activeColumns = 0;
            var longestRun = 0;
            var currentRun = 0;
            var left = maskWidth;
            var right = 0;
            var fragmentedColumns = 0;
            for (var x = 0; x < maskWidth; x++)
            {
                var count = 0;
                var transitions = 0;
                var wasActive = false;
                for (var y = bestStart; y < bestEnd; y++)
                {
                    var active = mask.At<byte>(y, x) > 0;
                    if (active)
                        count++;

                    if (active && !wasActive)
                        transitions++;

                    wasActive = active;
                }

                if (count >= 2)
                {
                    activeColumns++;
                    currentRun++;
                    longestRun = Math.Max(longestRun, currentRun);
                    left = Math.Min(left, x);
                    right = Math.Max(right, x + 1);
                    if (transitions > 2)
                        fragmentedColumns++;
                }
                else
                {
                    currentRun = 0;
                }
            }

            var activeRows = rowCounts.Where(x => x >= threshold).OrderBy(x => x).ToArray();
            return new Metrics(
                yellowPixels,
                yellowPixels / (double)Math.Max(1, maskWidth * maskHeight),
                bestStart,
                bestEnd,
                bestEnd - bestStart,
                bestScore,
                activeColumns,
                longestRun,
                left == maskWidth ? 0 : left,
                right,
                right - (left == maskWidth ? right : left),
                rowCounts.Length == 0 ? 0 : rowCounts.Max(),
                activeRows.Length == 0 ? 0 : activeRows[activeRows.Length / 2],
                fragmentedColumns);
        }

        private static bool IsYellowTextPixel(int r, int g, int b)
        {
            return
                r >= 145 &&
                g >= 120 &&
                b <= 115 &&
                r >= g - 25 &&
                r <= g + 70 &&
                r >= b + 70 &&
                g >= b + 60;
        }

        private sealed record Metrics(
            int YellowPixels,
            double YellowRatio,
            int BandTop,
            int BandBottom,
            int BandRows,
            double BandScore,
            int ActiveColumns,
            int LongestColumnRun,
            int Left,
            int Right,
            int SpanWidth,
            int PeakRow,
            double MedianActiveRow,
            int FragmentedColumns);
    }
}
