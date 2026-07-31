using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public static class ImageFeatureExtractor
    {
        public static ImageFeatures Extract(Mat image)
        {
            using var gray = image.Channels() == 1
                ? image.Clone()
                : image.CvtColor(ColorConversionCodes.BGR2GRAY);

            using var blurred = gray.GaussianBlur(new Size(3, 3), 0);
            using var edges = blurred.Canny(80, 160);
            using var bright = gray.Threshold(220, 255, ThresholdTypes.Binary);

            var width = gray.Width;
            var height = gray.Height;
            var totalPixels = Math.Max(1, width * height);

            Cv2.MeanStdDev(gray, out var mean, out var stdDev);

            var edgeDensity = (double)Cv2.CountNonZero(edges) / totalPixels;
            var brightRatio = (double)Cv2.CountNonZero(bright) / totalPixels;

            var activeRows = 0;
            var longestRowRun = 0;
            var currentRowRun = 0;

            for (var y = 0; y < height; y++)
            {
                var rowCount = 0;

                for (var x = 0; x < width; x++)
                {
                    if (edges.At<byte>(y, x) > 0)
                        rowCount++;
                }

                var active = rowCount > width * 0.08;
                if (active)
                {
                    activeRows++;
                    currentRowRun++;
                    longestRowRun = Math.Max(longestRowRun, currentRowRun);
                }
                else
                {
                    currentRowRun = 0;
                }
            }

            var activeColumns = 0;

            for (var x = 0; x < width; x++)
            {
                var columnCount = 0;

                for (var y = 0; y < height; y++)
                {
                    if (edges.At<byte>(y, x) > 0)
                        columnCount++;
                }

                if (columnCount > height * 0.05)
                    activeColumns++;
            }

            return new ImageFeatures(
                width,
                height,
                mean.Val0,
                stdDev.Val0,
                edgeDensity,
                brightRatio,
                (double)activeRows / Math.Max(1, height),
                (double)longestRowRun / Math.Max(1, height),
                (double)activeColumns / Math.Max(1, width));
        }
    }
}
