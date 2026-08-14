using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    internal sealed class TalkatooPromptSignature
    {
        private const int YellowStartX = 80;
        private const double StrictMinimumMaskIoU = 0.90;
        private const double AdaptiveMinimumMaskIoU = 0.65;
        private const double StrictMaximumPixelChangeRatio = 0.03;
        private const double AdaptiveMaximumPixelChangeRatio = 0.08;
        private const int AdaptiveMaximumTranslation = 2;

        private TalkatooPromptSignature(
            int width,
            int height,
            byte[] yellowMask,
            int yellowPixelCount,
            Rect textBounds,
            Rect markerBounds,
            bool adaptive)
        {
            Width = width;
            Height = height;
            YellowMask = yellowMask;
            YellowPixelCount = yellowPixelCount;
            TextBounds = textBounds;
            MarkerBounds = markerBounds;
            Adaptive = adaptive;
        }

        public int Width { get; }
        public int Height { get; }
        public byte[] YellowMask { get; }
        public int YellowPixelCount { get; }
        public Rect TextBounds { get; }
        public Rect MarkerBounds { get; }
        public bool Adaptive { get; }

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
                        if (!TalkatooStaticGate.IsYellow(pixel))
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
                TalkatooStaticGate.FindMarkerBounds(image),
                adaptive: false);
        }

        public static TalkatooPromptSignature CaptureAdaptive(
            Mat image,
            TalkatooAdaptiveAnalysis analysis)
        {
            using var adjusted = new Mat();
            image.ConvertTo(adjusted, image.Type(), analysis.Gain);

            var width = adjusted.Width;
            var height = adjusted.Height;
            var mask = new byte[Math.Max(0, width * height)];
            var yellowPixels = 0;
            var textBounds = ClipBounds(analysis.Gate.TextBounds, width, height);

            for (var y = textBounds.Top; y < textBounds.Bottom; y++)
            {
                for (var x = textBounds.Left; x < textBounds.Right; x++)
                {
                    if (!IsAdaptiveSignatureYellow(adjusted.At<Vec3b>(y, x)))
                        continue;

                    mask[y * width + x] = 1;
                    yellowPixels++;
                }
            }

            return new TalkatooPromptSignature(
                width,
                height,
                mask,
                yellowPixels,
                textBounds,
                analysis.Gate.MarkerBounds,
                adaptive: true);
        }

        public bool IsNearIdenticalTo(TalkatooPromptSignature other)
        {
            if (Width != other.Width || Height != other.Height)
                return false;

            if (Adaptive != other.Adaptive)
                return false;

            var minimumMaskIoU = Adaptive
                ? AdaptiveMinimumMaskIoU
                : StrictMinimumMaskIoU;
            var maximumOffset = Adaptive ? AdaptiveMaximumTranslation : 0;
            if (MaskIoU(other, maximumOffset) < minimumMaskIoU)
                return false;

            var maximumPixelCount = Math.Max(YellowPixelCount, other.YellowPixelCount);
            var pixelChangeRatio = maximumPixelCount == 0
                ? 0
                : Math.Abs(YellowPixelCount - other.YellowPixelCount) / (double)maximumPixelCount;

            var maximumPixelChangeRatio = Adaptive
                ? AdaptiveMaximumPixelChangeRatio
                : StrictMaximumPixelChangeRatio;
            return pixelChangeRatio <= maximumPixelChangeRatio &&
                GeometryWithin(TextBounds, other.TextBounds, 2) &&
                GeometryWithin(MarkerBounds, other.MarkerBounds, 2);
        }

        private double MaskIoU(
            TalkatooPromptSignature other,
            int maximumOffset)
        {
            var best = 0.0;
            for (var offsetY = -maximumOffset;
                 offsetY <= maximumOffset;
                 offsetY++)
            {
                for (var offsetX = -maximumOffset;
                     offsetX <= maximumOffset;
                     offsetX++)
                {
                    var intersection = ShiftedIntersection(
                        other,
                        offsetX,
                        offsetY);
                    var union = YellowPixelCount +
                        other.YellowPixelCount -
                        intersection;
                    var iou = union == 0
                        ? 1
                        : intersection / (double)union;
                    best = Math.Max(best, iou);
                }
            }

            return best;
        }

        private int ShiftedIntersection(
            TalkatooPromptSignature other,
            int offsetX,
            int offsetY)
        {
            var intersection = 0;
            for (var y = TextBounds.Top; y < TextBounds.Bottom; y++)
            {
                var otherY = y + offsetY;
                if (otherY < 0 || otherY >= Height)
                    continue;

                for (var x = TextBounds.Left; x < TextBounds.Right; x++)
                {
                    if (YellowMask[y * Width + x] == 0)
                        continue;

                    var otherX = x + offsetX;
                    if (otherX < 0 || otherX >= Width)
                        continue;

                    if (other.YellowMask[otherY * Width + otherX] != 0)
                        intersection++;
                }
            }

            return intersection;
        }

        private static bool GeometryWithin(Rect first, Rect second, int tolerance)
        {
            return Math.Abs(first.X - second.X) <= tolerance &&
                Math.Abs(first.Y - second.Y) <= tolerance &&
                Math.Abs(first.Width - second.Width) <= tolerance &&
                Math.Abs(first.Height - second.Height) <= tolerance;
        }

        private static Rect ClipBounds(Rect bounds, int width, int height)
        {
            var left = Math.Clamp(bounds.Left, 0, width);
            var top = Math.Clamp(bounds.Top, 0, height);
            var right = Math.Clamp(bounds.Right, left, width);
            var bottom = Math.Clamp(bounds.Bottom, top, height);
            return new Rect(left, top, right - left, bottom - top);
        }

        private static bool IsAdaptiveSignatureYellow(Vec3b pixel)
        {
            if (!TalkatooStaticGate.IsYellow(pixel))
                return false;

            var green = pixel.Item1;
            var red = pixel.Item2;
            return red >= green - 25 && red <= green + 70;
        }

    }
}
