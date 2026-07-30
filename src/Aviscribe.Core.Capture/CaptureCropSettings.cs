using OpenCvSharp;
using System;

namespace Aviscribe.Core.Capture
{
    public sealed class CaptureCropSettings
    {
        public const int ReferenceWidth = 1920;
        public const int ReferenceHeight = 1080;
        public const int AspectWidth = 16;
        public const int AspectHeight = 9;

        public int SourceWidth { get; set; } = ReferenceWidth;
        public int SourceHeight { get; set; } = ReferenceHeight;
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; } = ReferenceWidth;
        public int Height { get; set; } = ReferenceHeight;

        public static CaptureCropSettings Default => new CaptureCropSettings();

        public CaptureCropSettings Clone()
        {
            return new CaptureCropSettings
            {
                SourceWidth = SourceWidth,
                SourceHeight = SourceHeight,
                X = X,
                Y = Y,
                Width = Width,
                Height = Height
            };
        }

        public Rect Resolve(int actualSourceWidth, int actualSourceHeight)
        {
            if (actualSourceWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(actualSourceWidth));
            if (actualSourceHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(actualSourceHeight));

            if (!HasUsableBounds())
                return LargestCenteredCrop(actualSourceWidth, actualSourceHeight);

            var scaleX = actualSourceWidth / (double)SourceWidth;
            var scaleY = actualSourceHeight / (double)SourceHeight;
            var scaledX = (int)Math.Round(X * scaleX);
            var scaledY = (int)Math.Round(Y * scaleY);
            var scaledWidth = Math.Max(AspectWidth, (int)Math.Round(Width * scaleX));
            var scaledHeight = Math.Max(AspectHeight, (int)Math.Round(Height * scaleY));

            var centerX = scaledX + scaledWidth / 2.0;
            var centerY = scaledY + scaledHeight / 2.0;
            var aspectScale = Math.Max(
                1,
                (int)Math.Floor(Math.Min(
                    scaledWidth / (double)AspectWidth,
                    scaledHeight / (double)AspectHeight)));
            aspectScale = Math.Min(
                aspectScale,
                Math.Min(actualSourceWidth / AspectWidth, actualSourceHeight / AspectHeight));

            if (aspectScale < 1)
                return new Rect(0, 0, actualSourceWidth, actualSourceHeight);

            var width = aspectScale * AspectWidth;
            var height = aspectScale * AspectHeight;
            var x = Clamp((int)Math.Round(centerX - width / 2.0), 0, actualSourceWidth - width);
            var y = Clamp((int)Math.Round(centerY - height / 2.0), 0, actualSourceHeight - height);
            return new Rect(x, y, width, height);
        }

        public static CaptureCropSettings FromRect(
            int sourceWidth,
            int sourceHeight,
            Rect requestedBounds)
        {
            if (sourceWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            if (sourceHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceHeight));

            var boundedWidth = Clamp(requestedBounds.Width, AspectWidth, sourceWidth);
            var boundedHeight = Clamp(requestedBounds.Height, AspectHeight, sourceHeight);
            var aspectScale = Math.Max(
                1,
                (int)Math.Floor(Math.Min(
                    boundedWidth / (double)AspectWidth,
                    boundedHeight / (double)AspectHeight)));
            aspectScale = Math.Min(
                aspectScale,
                Math.Min(sourceWidth / AspectWidth, sourceHeight / AspectHeight));

            if (aspectScale < 1)
            {
                return new CaptureCropSettings
                {
                    SourceWidth = sourceWidth,
                    SourceHeight = sourceHeight,
                    X = 0,
                    Y = 0,
                    Width = sourceWidth,
                    Height = sourceHeight
                };
            }

            var width = aspectScale * AspectWidth;
            var height = aspectScale * AspectHeight;
            var centerX = requestedBounds.X + requestedBounds.Width / 2.0;
            var centerY = requestedBounds.Y + requestedBounds.Height / 2.0;
            var x = Clamp((int)Math.Round(centerX - width / 2.0), 0, sourceWidth - width);
            var y = Clamp((int)Math.Round(centerY - height / 2.0), 0, sourceHeight - height);

            return new CaptureCropSettings
            {
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                X = x,
                Y = y,
                Width = width,
                Height = height
            };
        }

        public static CaptureCropSettings CreateLargestCentered(
            int sourceWidth,
            int sourceHeight)
        {
            var bounds = LargestCenteredCrop(sourceWidth, sourceHeight);
            return new CaptureCropSettings
            {
                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height
            };
        }

        private bool HasUsableBounds()
        {
            return SourceWidth > 0 &&
                SourceHeight > 0 &&
                Width > 0 &&
                Height > 0 &&
                X >= 0 &&
                Y >= 0 &&
                X + Width <= SourceWidth &&
                Y + Height <= SourceHeight;
        }

        private static Rect LargestCenteredCrop(int sourceWidth, int sourceHeight)
        {
            if (sourceWidth <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceWidth));
            if (sourceHeight <= 0)
                throw new ArgumentOutOfRangeException(nameof(sourceHeight));

            var aspectScale = Math.Min(
                sourceWidth / AspectWidth,
                sourceHeight / AspectHeight);
            if (aspectScale < 1)
                return new Rect(0, 0, sourceWidth, sourceHeight);

            var width = aspectScale * AspectWidth;
            var height = aspectScale * AspectHeight;
            return new Rect(
                (sourceWidth - width) / 2,
                (sourceHeight - height) / 2,
                width,
                height);
        }

        private static int Clamp(int value, int minimum, int maximum)
        {
            return Math.Min(Math.Max(value, minimum), maximum);
        }
    }
}
