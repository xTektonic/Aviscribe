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
                TalkatooStaticGate.FindMarkerBounds(image));
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

    }
}
