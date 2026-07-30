using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    internal sealed class TalkatooPromptSignature
    {
        private const int YellowStartX = 80;

        private TalkatooPromptSignature(
            int width,
            int height,
            byte[] yellowMask,
            int yellowPixelCount,
            Rect textBounds,
            Rect markerBounds)
        {
            Width = width;
            Height = height;
            YellowMask = yellowMask;
            YellowPixelCount = yellowPixelCount;
            TextBounds = textBounds;
            MarkerBounds = markerBounds;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] YellowMask { get; }
        public int YellowPixelCount { get; }
        public Rect TextBounds { get; }
        public Rect MarkerBounds { get; }

        public static TalkatooPromptSignature Capture(Mat image)
        {
            var width = image.Width;
            var height = image.Height;
            var mask = new byte[Math.Max(0, width * height)];
            var yellowPixels = 0;
            var left = width;
            var top = height;
            var right = 0;
            var bottom = 0;

            if (!image.Empty() && image.Channels() >= 3)
            {
                for (var y = 0; y < height; y++)
                {
                    for (var x = YellowStartX; x < width; x++)
                    {
                        var pixel = image.At<Vec3b>(y, x);
                        var b = pixel.Item0;
                        var g = pixel.Item1;
                        var r = pixel.Item2;
                        var isYellow =
                            r >= 145 &&
                            g >= 120 &&
                            b <= 115 &&
                            r >= g - 25 &&
                            r <= g + 70 &&
                            r >= b + 70 &&
                            g >= b + 60;

                        if (!isYellow)
                            continue;

                        mask[y * width + x] = 1;
                        yellowPixels++;
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x + 1);
                        bottom = Math.Max(bottom, y + 1);
                    }
                }
            }

            var textBounds = yellowPixels == 0
                ? default
                : new Rect(left, top, right - left, bottom - top);

            return new TalkatooPromptSignature(
                width,
                height,
                mask,
                yellowPixels,
                textBounds,
                FindMarkerBounds(image));
        }

        public bool IsNearIdenticalTo(TalkatooPromptSignature other)
        {
            if (Width != other.Width || Height != other.Height)
                return false;

            if (MaskIoU(other) < 0.90)
                return false;

            var maximumPixelCount = Math.Max(YellowPixelCount, other.YellowPixelCount);
            var pixelChangeRatio = maximumPixelCount == 0
                ? 0
                : Math.Abs(YellowPixelCount - other.YellowPixelCount) / (double)maximumPixelCount;

            return pixelChangeRatio <= 0.03 &&
                GeometryWithin(TextBounds, other.TextBounds, 2) &&
                GeometryWithin(MarkerBounds, other.MarkerBounds, 2);
        }

        private double MaskIoU(TalkatooPromptSignature other)
        {
            var intersection = 0;
            var union = 0;

            for (var i = 0; i < YellowMask.Length; i++)
            {
                var current = YellowMask[i] != 0;
                var candidate = other.YellowMask[i] != 0;

                if (current && candidate)
                    intersection++;

                if (current || candidate)
                    union++;
            }

            return union == 0 ? 1 : intersection / (double)union;
        }

        private static bool GeometryWithin(Rect first, Rect second, int tolerance)
        {
            return Math.Abs(first.X - second.X) <= tolerance &&
                Math.Abs(first.Y - second.Y) <= tolerance &&
                Math.Abs(first.Width - second.Width) <= tolerance &&
                Math.Abs(first.Height - second.Height) <= tolerance;
        }

        private static Rect FindMarkerBounds(Mat image)
        {
            if (image.Empty() || image.Channels() < 3)
                return default;

            var searchWidth = Math.Min(image.Width, 96);
            using var whiteMask = new Mat(
                new Size(searchWidth, image.Height),
                MatType.CV_8UC1,
                Scalar.Black);

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < searchWidth; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var maximum = Math.Max(pixel.Item2, Math.Max(pixel.Item1, pixel.Item0));
                    var minimum = Math.Min(pixel.Item2, Math.Min(pixel.Item1, pixel.Item0));

                    if (maximum >= 205 && minimum >= 165 && maximum - minimum <= 70)
                        whiteMask.Set(y, x, 255);
                }
            }

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var componentCount = Cv2.ConnectedComponentsWithStats(
                whiteMask,
                labels,
                stats,
                centroids);
            var bestBounds = default(Rect);
            var bestArea = 0;

            for (var index = 1; index < componentCount; index++)
            {
                var area = stats.Get<int>(index, (int)ConnectedComponentsTypes.Area);
                if (area < 150 || area <= bestArea)
                    continue;

                bestArea = area;
                bestBounds = new Rect(
                    stats.Get<int>(index, (int)ConnectedComponentsTypes.Left),
                    stats.Get<int>(index, (int)ConnectedComponentsTypes.Top),
                    stats.Get<int>(index, (int)ConnectedComponentsTypes.Width),
                    stats.Get<int>(index, (int)ConnectedComponentsTypes.Height));
            }

            return bestBounds;
        }
    }
}
