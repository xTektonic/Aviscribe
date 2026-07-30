using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    internal static class TalkatooStaticGate
    {
        private const int TextBandHeight = 34;

        public static TalkatooGateResult Analyze(Mat image)
        {
            if (image.Empty() || image.Channels() < 3)
                return default;

            var markerCandidates = FindMarkerCandidates(image);
            if (markerCandidates.Count == 0)
                return default;

            var yellowMask = CreateYellowMask(image, out var totalYellowPixels);
            TalkatooTextBand bestBand = default;
            Rect bestMarker = default;

            foreach (var marker in markerCandidates)
            {
                var band = FindBestTextBand(
                    yellowMask,
                    image.Width,
                    image.Height,
                    marker);

                if (!band.Present || band.YellowPixels <= bestBand.YellowPixels)
                    continue;

                bestMarker = marker;
                bestBand = band;
            }

            return new TalkatooGateResult(
                bestBand.Present,
                bestMarker,
                bestBand.Bounds,
                bestBand.YellowPixels,
                bestBand.ActiveColumns,
                bestBand.Occupancy,
                totalYellowPixels);
        }

        public static Rect FindMarkerBounds(Mat image)
        {
            if (image.Empty() || image.Channels() < 3)
                return default;

            var candidates = FindMarkerCandidates(image);
            return candidates.Count == 0 ? default : candidates[0];
        }

        public static bool IsYellow(Vec3b pixel)
        {
            var b = pixel.Item0;
            var g = pixel.Item1;
            var r = pixel.Item2;

            return
                r >= 220 &&
                g >= 200 &&
                b <= 130 &&
                r >= b + 80 &&
                g >= b + 70;
        }

        private static byte[] CreateYellowMask(Mat image, out int yellowPixels)
        {
            var mask = new byte[image.Width * image.Height];
            yellowPixels = 0;

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    if (!IsYellow(image.At<Vec3b>(y, x)))
                        continue;

                    mask[y * image.Width + x] = 1;
                    yellowPixels++;
                }
            }

            return mask;
        }

        private static List<Rect> FindMarkerCandidates(Mat image)
        {
            using var whiteMask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.Black);

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
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
            var candidates = new List<(Rect Bounds, int Area)>();

            for (var index = 1; index < componentCount; index++)
            {
                var bounds = new Rect(
                    stats.Get<int>(index, (int)ConnectedComponentsTypes.Left),
                    stats.Get<int>(index, (int)ConnectedComponentsTypes.Top),
                    stats.Get<int>(index, (int)ConnectedComponentsTypes.Width),
                    stats.Get<int>(index, (int)ConnectedComponentsTypes.Height));
                var area = stats.Get<int>(index, (int)ConnectedComponentsTypes.Area);
                var fill = area / (double)Math.Max(1, bounds.Width * bounds.Height);

                var fullMarker =
                    bounds.Y <= 2 &&
                    bounds.Width is >= 45 and <= 60 &&
                    bounds.Height is >= 38 and <= 46 &&
                    area is >= 650 and <= 1200 &&
                    fill is >= 0.30 and <= 0.55;
                var leftClippedMarker =
                    bounds.X == 0 &&
                    bounds.Width is >= 10 and <= 22 &&
                    bounds.Height is >= 36 and <= 46 &&
                    area is >= 150 and <= 350 &&
                    fill is >= 0.25 and <= 0.60;

                if (fullMarker || leftClippedMarker)
                    candidates.Add((bounds, area));
            }

            return candidates
                .OrderByDescending(candidate => candidate.Area)
                .Select(candidate => candidate.Bounds)
                .ToList();
        }

        private static TalkatooTextBand FindBestTextBand(
            byte[] yellowMask,
            int width,
            int height,
            Rect marker)
        {
            if (width <= 0 || height <= 0)
                return default;

            var bandHeight = Math.Min(TextBandHeight, height);
            var maximumBandTop = Math.Max(0, height - bandHeight);
            TalkatooTextBand best = default;
            var columnCounts = new int[width];
            var pixelPrefix = new int[width + 1];
            var activePrefix = new int[width + 1];

            for (var bandTop = 0; bandTop <= maximumBandTop; bandTop++)
            {
                var bandBottom = bandTop + bandHeight;
                Array.Clear(columnCounts);

                for (var x = Math.Max(0, marker.Right); x < width; x++)
                {
                    for (var y = bandTop; y < bandBottom; y++)
                    {
                        if (yellowMask[y * width + x] != 0)
                            columnCounts[x]++;
                    }
                }

                for (var x = 0; x < width; x++)
                {
                    pixelPrefix[x + 1] = pixelPrefix[x] + columnCounts[x];
                    activePrefix[x + 1] = activePrefix[x] + (columnCounts[x] > 0 ? 1 : 0);
                }

                var lastCandidateStart = Math.Min(width - 1, marker.Right + 75);
                for (var left = Math.Max(0, marker.Right);
                     left <= lastCandidateStart;
                     left++)
                {
                    if (columnCounts[left] == 0)
                        continue;

                    var limit = Math.Min(width, left + 575);
                    var right = limit;
                    while (right > left && columnCounts[right - 1] == 0)
                        right--;

                    var spanWidth = right - left;
                    var yellowPixels = pixelPrefix[right] - pixelPrefix[left];
                    var activeColumns = activePrefix[right] - activePrefix[left];
                    var occupancy = yellowPixels /
                        (double)Math.Max(1, spanWidth * bandHeight);
                    var present =
                        yellowPixels >= 500 &&
                        activeColumns >= 35 &&
                        spanWidth <= 575 &&
                        occupancy >= 0.15;

                    if (!present || yellowPixels <= best.YellowPixels)
                        continue;

                    best = new TalkatooTextBand(
                        true,
                        new Rect(left, bandTop, spanWidth, bandHeight),
                        yellowPixels,
                        activeColumns,
                        occupancy);
                }
            }

            return best;
        }

        private readonly record struct TalkatooTextBand(
            bool Present,
            Rect Bounds,
            int YellowPixels,
            int ActiveColumns,
            double Occupancy);
    }

    internal readonly record struct TalkatooGateResult(
        bool Present,
        Rect MarkerBounds,
        Rect TextBounds,
        int YellowPixels,
        int ActiveColumns,
        double Occupancy,
        int TotalYellowPixels);
}
