using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public class TextDetection
    {
        //image.SaveImage("[removed]");

        public static bool HasTalkatooText(Mat image)
        {
            return HasYellowTalkatooText(image);
        }

        public static bool HasYellowTalkatooText(Mat image)
        {
            if (image.Empty() || image.Channels() < 3)
                return false;

            if (HasWideWhiteOverlayText(image))
                return false;

            using var mask = CreateYellowGlyphMask(image);
            var metrics = MeasureYellowText(mask);
            var hasWhiteMarker = HasWhiteTalkatooMoonMarker(image);
            var whiteSupport = MeasureTextWhiteSupport(image);
            var yellowPixels = Cv2.CountNonZero(mask);

            if (hasWhiteMarker && (metrics.HasTextBand || metrics.HasWhiteMarkerTextBand))
                return true;

            if (metrics.HasTextBand)
            {
                return true;
            }

            if ((metrics.HasMarkerSupportedTextBand ||
                 metrics.HasOutlinedTextBand ||
                 metrics.HasStrongOutlinedTextBand) &&
                whiteSupport.Pixels >= 120 &&
                whiteSupport.ActiveColumns >= 20)
            {
                return true;
            }

            return
                yellowPixels >= 650 &&
                whiteSupport.Pixels >= 120 &&
                whiteSupport.ActiveColumns >= 20 &&
                metrics.LongestColumnRun <= 220 &&
                HasTalkatooText_v3(image);
        }

        private static YellowTextMetrics MeasureYellowText(Mat mask)
        {
            var rowCounts = new int[mask.Height];
            var yellowPixels = 0;

            for (var y = 0; y < mask.Height; y++)
            {
                var count = 0;

                for (var x = 0; x < mask.Width; x++)
                {
                    if (mask.At<byte>(y, x) > 0)
                        count++;
                }

                rowCounts[y] = count;
                yellowPixels += count;
            }

            var rowThreshold = Math.Max(18.0, mask.Width * 0.04);
            var bandTop = 0;
            var bandBottom = 0;
            var bestBandScore = 0.0;

            for (var start = 0; start < mask.Height; start++)
            {
                var score = 0.0;
                for (var end = start; end < Math.Min(mask.Height, start + 34); end++)
                {
                    score += Math.Max(0, rowCounts[end] - rowThreshold);

                    if (end - start + 1 >= 8 && score > bestBandScore)
                    {
                        bestBandScore = score;
                        bandTop = start;
                        bandBottom = end + 1;
                    }
                }
            }

            var activeColumns = 0;
            var longestColumnRun = 0;
            var currentColumnRun = 0;
            var left = mask.Width;
            var right = 0;
            var fragmentedColumns = 0;

            for (var x = 0; x < mask.Width; x++)
            {
                var count = 0;
                var transitions = 0;
                var wasActive = false;

                for (var y = bandTop; y < bandBottom; y++)
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
                    currentColumnRun++;
                    longestColumnRun = Math.Max(longestColumnRun, currentColumnRun);
                    left = Math.Min(left, x);
                    right = Math.Max(right, x + 1);

                    if (transitions > 2)
                        fragmentedColumns++;
                }
                else
                {
                    currentColumnRun = 0;
                }
            }

            var activeRows = rowCounts.Where(x => x >= rowThreshold).OrderBy(x => x).ToArray();
            var medianActiveRow = activeRows.Length == 0 ? 0 : activeRows[activeRows.Length / 2];
            var totalPixels = Math.Max(1, mask.Width * mask.Height);
            var spanWidth = right - (left == mask.Width ? right : left);
            var yellowRatio = yellowPixels / (double)totalPixels;
            var activeColumnDensity = activeColumns / (double)Math.Max(1, spanWidth);

            var hasTextBand =
                yellowPixels >= 650 &&
                yellowRatio >= 0.035 &&
                bestBandScore >= 700 &&
                activeColumns >= 85 &&
                spanWidth >= 145 &&
                spanWidth <= 470 &&
                fragmentedColumns >= 90 &&
                longestColumnRun <= 180 &&
                medianActiveRow >= 42;

            var hasWhiteMarkerTextBand =
                yellowPixels >= 650 &&
                yellowRatio >= 0.035 &&
                bestBandScore >= 700 &&
                activeColumns >= 80 &&
                spanWidth >= 120 &&
                spanWidth <= 575 &&
                fragmentedColumns >= 45 &&
                medianActiveRow >= 42;

            var hasMarkerSupportedTextBand =
                yellowPixels >= 650 &&
                yellowRatio >= 0.035 &&
                bestBandScore >= 700 &&
                activeColumns >= 85 &&
                spanWidth >= 145 &&
                spanWidth <= 520 &&
                (left == mask.Width ? 0 : left) >= 95 &&
                fragmentedColumns >= 20 &&
                longestColumnRun <= 180 &&
                medianActiveRow >= 42;

            var hasOutlinedTextBand =
                yellowPixels >= 650 &&
                yellowRatio >= 0.035 &&
                bestBandScore >= 700 &&
                activeColumns >= 85 &&
                activeColumnDensity >= 0.45 &&
                spanWidth >= 125 &&
                spanWidth <= 575 &&
                (left == mask.Width ? 0 : left) >= 90 &&
                medianActiveRow >= 42;

            var hasStrongOutlinedTextBand =
                yellowPixels >= 4000 &&
                yellowRatio >= 0.08 &&
                bestBandScore >= 2000 &&
                activeColumns >= 140 &&
                activeColumnDensity >= 0.45 &&
                spanWidth >= 125 &&
                spanWidth <= 575 &&
                longestColumnRun <= 220 &&
                (left == mask.Width ? 0 : left) >= 90 &&
                medianActiveRow >= 42;

            return new YellowTextMetrics(
                hasTextBand,
                hasWhiteMarkerTextBand,
                hasMarkerSupportedTextBand,
                hasOutlinedTextBand,
                hasStrongOutlinedTextBand,
                left == mask.Width ? 0 : left,
                spanWidth,
                longestColumnRun,
                fragmentedColumns);
        }

        private static bool HasBrightYellowOutlinedTalkatooText(Mat image)
        {
            var whiteSupport = MeasureTextWhiteSupport(image);
            var hasWhiteMarker = HasWhiteTalkatooMoonMarker(image);
            return HasBrightYellowOutlinedTalkatooText(
                image,
                hasWhiteMarker || HasTalkatooMoonMarker(image),
                hasWhiteMarker,
                whiteSupport);
        }

        private static bool HasBrightYellowOutlinedTalkatooText(
            Mat image,
            bool hasMarker,
            bool hasWhiteMarker,
            WhiteSupportMetrics whiteSupport)
        {
            if (!hasMarker)
                return false;

            if (!hasWhiteMarker && (whiteSupport.Pixels < 520 || whiteSupport.ActiveColumns < 42))
                return false;

            using var mask = CreateBrightYellowGlyphMask(image);
            var metrics = MeasureBrightYellowText(mask, image);

            return
                metrics.BrightPixels >= 850 &&
                metrics.BrightRatio >= 0.025 &&
                metrics.BandScore >= 520 &&
                metrics.ActiveColumns >= 70 &&
                metrics.SpanWidth >= 90 &&
                metrics.SpanWidth <= 575 &&
                metrics.BandRows >= 8 &&
                metrics.BandCenterRatio >= 0.20 &&
                metrics.LongestColumnRun <= 180 &&
                metrics.FragmentedColumns >= 45 &&
                metrics.DarkSupportedPixels >= 70 &&
                metrics.DarkSupportedColumns >= 35;
        }

        private static BrightYellowTextMetrics MeasureBrightYellowText(Mat mask, Mat image)
        {
            var rowCounts = new int[mask.Height];
            var brightPixels = 0;

            for (var y = 0; y < mask.Height; y++)
            {
                var count = 0;
                for (var x = 0; x < mask.Width; x++)
                {
                    if (mask.At<byte>(y, x) > 0)
                        count++;
                }

                rowCounts[y] = count;
                brightPixels += count;
            }

            var rowThreshold = Math.Max(14.0, mask.Width * 0.028);
            var bandTop = 0;
            var bandBottom = 0;
            var bestBandScore = 0.0;

            for (var start = 0; start < mask.Height; start++)
            {
                var score = 0.0;
                for (var end = start; end < Math.Min(mask.Height, start + 34); end++)
                {
                    score += Math.Max(0, rowCounts[end] - rowThreshold);

                    if (end - start + 1 >= 8 && score > bestBandScore)
                    {
                        bestBandScore = score;
                        bandTop = start;
                        bandBottom = end + 1;
                    }
                }
            }

            var activeColumns = 0;
            var longestColumnRun = 0;
            var currentColumnRun = 0;
            var fragmentedColumns = 0;
            var darkSupportedColumns = new bool[mask.Width];
            var darkSupportedPixels = 0;
            var left = mask.Width;
            var right = 0;
            var darkIntegral = BuildDarkIntegral(image, maxValue: 120);

            for (var x = 0; x < mask.Width; x++)
            {
                var count = 0;
                var transitions = 0;
                var wasActive = false;

                for (var y = bandTop; y < bandBottom; y++)
                {
                    var active = mask.At<byte>(y, x) > 0;
                    if (active && !wasActive)
                        transitions++;

                    wasActive = active;

                    if (!active)
                        continue;

                    count++;

                    if (HasNearbyDarkPixel(darkIntegral, image.Width, image.Height, x, y, radius: 2))
                    {
                        darkSupportedPixels++;
                        darkSupportedColumns[x] = true;
                    }
                }

                if (count >= 2)
                {
                    activeColumns++;
                    currentColumnRun++;
                    longestColumnRun = Math.Max(longestColumnRun, currentColumnRun);
                    left = Math.Min(left, x);
                    right = Math.Max(right, x + 1);

                    if (transitions >= 2)
                        fragmentedColumns++;
                }
                else
                {
                    currentColumnRun = 0;
                }
            }

            var spanWidth = right - (left == mask.Width ? right : left);
            var center = bandBottom <= bandTop
                ? 0
                : ((bandTop + bandBottom) / 2.0) / Math.Max(1, mask.Height);

            return new BrightYellowTextMetrics(
                brightPixels,
                brightPixels / (double)Math.Max(1, mask.Width * mask.Height),
                bestBandScore,
                bandBottom - bandTop,
                center,
                activeColumns,
                spanWidth,
                longestColumnRun,
                fragmentedColumns,
                darkSupportedPixels,
                darkSupportedColumns.Count(x => x));
        }

        private static bool HasTalkatooMoonMarker(Mat image)
        {
            var iconSearchWidth = Math.Min(image.Width, 92);
            using var markerMask = new Mat(new Size(iconSearchWidth, image.Height), MatType.CV_8UC1, Scalar.Black);
            var markerPixels = 0;

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < iconSearchWidth; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));

                    if (max >= 120 && (min >= 95 || max - min >= 45))
                    {
                        markerMask.Set(y, x, 255);
                        markerPixels++;
                    }
                }
            }

            if (markerPixels < 80 || markerPixels > iconSearchWidth * image.Height * 0.98)
                return false;

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            Cv2.MorphologyEx(markerMask, markerMask, MorphTypes.Close, kernel);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var componentCount = Cv2.ConnectedComponentsWithStats(markerMask, labels, stats, centroids);

            for (var i = 1; i < componentCount; i++)
            {
                var x = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                var y = stats.Get<int>(i, (int)ConnectedComponentsTypes.Top);
                var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                var area = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);

                var aspectRatio = width / (double)Math.Max(1, height);
                var centerX = x + width / 2.0;
                var centerY = y + height / 2.0;
                var fill = area / (double)Math.Max(1, width * height);

                if (area < 120 || area > 4500)
                    continue;

                if (width < 10 || width > 92 || height < 10 || height > image.Height)
                    continue;

                if (aspectRatio < 0.25 || aspectRatio > 3.5)
                    continue;

                if (fill < 0.20)
                    continue;

                if (centerX < 5 || centerX > 82 || centerY < 4 || centerY > image.Height - 2)
                    continue;

                return true;
            }

            return false;
        }

        private static bool HasLeftMarkerMass(Mat image)
        {
            var iconSearchWidth = Math.Min(image.Width, 96);
            using var markerMask = new Mat(new Size(iconSearchWidth, image.Height), MatType.CV_8UC1, Scalar.Black);

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < iconSearchWidth; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));

                    if (max >= 120 && (min >= 95 || max - min >= 45))
                        markerMask.Set(y, x, 255);
                }
            }

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Ellipse, new Size(3, 3));
            Cv2.MorphologyEx(markerMask, markerMask, MorphTypes.Close, kernel);

            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var componentCount = Cv2.ConnectedComponentsWithStats(markerMask, labels, stats, centroids);

            for (var i = 1; i < componentCount; i++)
            {
                var x = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                var area = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);
                var fill = area / (double)Math.Max(1, width * height);

                if (x > 12)
                    continue;

                if (width < 32 || height < 24 || area < 900)
                    continue;

                if (fill < 0.25)
                    continue;

                return true;
            }

            return false;
        }

        private static bool HasWhiteTalkatooMoonMarker(Mat image)
        {
            var iconSearchWidth = Math.Min(image.Width, 96);
            using var whiteMask = new Mat(new Size(iconSearchWidth, image.Height), MatType.CV_8UC1, Scalar.Black);
            var whitePixels = 0;

            for (var y = 0; y < image.Height; y++)
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

            if (whitePixels < 180)
                return false;

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
                var fill = area / (double)Math.Max(1, width * height);

                if (area < 160 || area > 2400)
                    continue;

                if (width < 12 || width > 70 || height < 12 || height > image.Height)
                    continue;

                if (fill < 0.18)
                    continue;

                if (x > 90 || y > image.Height - 8)
                    continue;

                return true;
            }

            return false;
        }

        private static WhiteSupportMetrics MeasureTextWhiteSupport(Mat image)
        {
            var whitePixels = 0;
            var activeColumns = 0;

            for (var x = 80; x < image.Width; x++)
            {
                var columnWhitePixels = 0;

                for (var y = 0; y < image.Height; y++)
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

            return new WhiteSupportMetrics(whitePixels, activeColumns);
        }

        private static bool HasWideWhiteOverlayText(Mat image)
        {
            var metrics = MeasureTextWhiteSupport(image);
            return metrics.Pixels >= 1800 || metrics.ActiveColumns >= 100;
        }

        private static Mat CreateYellowGlyphMask(Mat image)
        {
            var mask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.Black);

            for (var y = 0; y < image.Rows; y++)
            {
                for (var x = 0; x < image.Cols; x++)
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

                    if (x >= 80 && isYellow)
                        mask.Set(y, x, 255);
                }
            }

            return mask;
        }

        private static Mat CreateBrightYellowGlyphMask(Mat image)
        {
            var mask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.Black);

            for (var y = 0; y < image.Rows; y++)
            {
                for (var x = 80; x < image.Cols; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    var isBrightYellow =
                        r >= 205 &&
                        g >= 175 &&
                        b <= 150 &&
                        r >= b + 60 &&
                        g >= b + 45;

                    if (isBrightYellow)
                        mask.Set(y, x, 255);
                }
            }

            return mask;
        }

        // Single line
        public static bool HasTalkatooText_v3(Mat image)
        {
            using var gray = image.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var blurred = gray.GaussianBlur(new Size(3, 3), 0);
            using var edges = blurred.Canny(80, 160);

            int width = edges.Cols;
            int height = edges.Rows;

            int edgePixels = Cv2.CountNonZero(edges);
            double density = (double)edgePixels / (width * height);

            if (density < 0.015)
                return false;

            int longestRun = 0, currentRun = 0;
            int bandStart = 0, bestStart = 0;

            for (int y = 0; y < height; y++)
            {
                int rowCount = 0;

                for (int x = 0; x < width; x++)
                {
                    if (edges.At<byte>(y, x) > 0)
                        rowCount++;
                }

                bool active = rowCount > width * 0.08;

                if (active)
                {
                    if (currentRun == 0)
                        bandStart = y;

                    currentRun++;

                    if (currentRun > longestRun)
                    {
                        longestRun = currentRun;
                        bestStart = bandStart;
                    }
                }
                else
                {
                    currentRun = 0;
                }
            }

            if (longestRun < height * 0.5)
                return false;

            int bandCenter = bestStart + longestRun / 2;
            double offset = Math.Abs(bandCenter - height / 2.0) / height;

            if (offset > 0.2)
                return false;

            int activeCols = 0;

            for (int x = 0; x < width; x++)
            {
                int count = 0;

                for (int y = bestStart; y < bestStart + longestRun; y++)
                {
                    if (edges.At<byte>(y, x) > 0)
                        count++;
                }

                if (count > longestRun * 0.1)
                    activeCols++;
            }

            double colRatio = (double)activeCols / width;

            if (colRatio < 0.15)
                return false;

            return true;
        }

        // Single line
        public static bool HasTalkatooText_v2(Mat image)
        {
            using var gray = image.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var blurred = gray.GaussianBlur(new Size(3, 3), 0);
            using var edges = blurred.Canny(80, 160);

            int width = edges.Cols;
            int height = edges.Rows;

            int totalPixels = width * height;
            int edgePixels = Cv2.CountNonZero(edges);

            double density = (double)edgePixels / totalPixels;

            if (density < 0.01)
                return false;

            // Now MUCH cleaner row analysis
            int activeRows = 0;
            int longestRun = 0;
            int currentRun = 0;

            for (int y = 0; y < height; y++)
            {
                int rowCount = 0;

                for (int x = 0; x < width; x++)
                {
                    if (edges.At<byte>(y, x) > 0)
                        rowCount++;
                }

                bool isActive = rowCount > width * 0.08;

                if (isActive)
                {
                    activeRows++;
                    currentRun++;
                    longestRun = Math.Max(longestRun, currentRun);
                }
                else
                {
                    currentRun = 0;
                }
            }

            double rowRatio = (double)activeRows / height;

            // Because we cropped, tighten constraints
            if (rowRatio > 1) return false; // still too thick → dialogue
            if (rowRatio < 0.5) return false; // too sparse

            if (longestRun < height * 0.25)
                return false;
            return true;
        }

        // Multiline version
        public static bool HasTalkatooText_v1(Mat image)
        {
            // 1. Preprocess
            using var gray = image.CvtColor(ColorConversionCodes.BGR2GRAY);

            // Light blur helps reduce noise from background
            using var blurred = gray.GaussianBlur(new Size(3, 3), 0);

            using var edges = blurred.Canny(80, 160);

            int width = edges.Cols;
            int height = edges.Rows;

            int totalPixels = width * height;
            int edgePixels = Cv2.CountNonZero(edges);

            double density = (double)edgePixels / totalPixels;

            // Too little structure, no text
            if (density < 0.05 || density > 0.1)
                return false;

            // 2. Analyze row distribution
            int activeRows = 0;
            int longestRun = 0;
            int currentRun = 0;

            for (int y = 0; y < height; y++)
            {
                int rowCount = 0;

                for (int x = 0; x < width; x++)
                {
                    if (edges.At<byte>(y, x) > 0)
                        rowCount++;
                }

                bool isActive = rowCount > width * 0.08; // 8% of row has edges

                if (isActive)
                {
                    activeRows++;
                    currentRun++;
                    if (currentRun > longestRun)
                        longestRun = currentRun;
                }
                else
                {
                    currentRun = 0;
                }
            }

            double rowRatio = (double)activeRows / height;

            // ----------------------------
            // KEY FILTERS
            // ----------------------------

            // Too many rows, probably dialogue (multi-line)
            if (rowRatio > 0.5)
                return false;

            // Too few rows, probably noise
            if (rowRatio < 0.05)
                return false;

            // Must have a continuous horizontal band (single line)
            if (longestRun < height * 0.15)
                return false;

            // 3. Column continuity (ensures it's actually text, not scattered noise)
            int activeCols = 0;

            for (int x = 0; x < width; x++)
            {
                int colCount = 0;

                for (int y = 0; y < height; y++)
                {
                    if (edges.At<byte>(y, x) > 0)
                        colCount++;
                }

                if (colCount > height * 0.05)
                    activeCols++;
            }

            double colRatio = (double)activeCols / width;

            // Not wide enough, probably not a moon name
            if (colRatio < 0.2)
                return false;

            //image.SaveImage("[removed]");
            return true;
        }

        public static bool HasMoonText(Mat image)
        {
            return HasMoonGetText(image);
        }

        private readonly record struct YellowTextMetrics(
            bool HasTextBand,
            bool HasWhiteMarkerTextBand,
            bool HasMarkerSupportedTextBand,
            bool HasOutlinedTextBand,
            bool HasStrongOutlinedTextBand,
            int Left,
            int SpanWidth,
            int LongestColumnRun,
            int FragmentedColumns);

        private readonly record struct WhiteSupportMetrics(int Pixels, int ActiveColumns);

        public static bool HasMoonGetText(Mat image)
        {
            if (image.Empty() || image.Channels() < 3)
                return false;

            if (image.Height > 100)
            {
                if (HasMoonGetBannerText(image))
                    return true;

                if (!HasLargeMoonGetCelebrationMass(image))
                    return false;

                return HasLargeMoonGetCelebrationText(MeasureMoonGetText(image));
            }

            var metrics = MeasureMoonGetText(image);

            var strictOutlinedText =
                metrics.WhitePixels >= 1800 &&
                metrics.WhiteRatio >= 0.035 &&
                metrics.BandScore >= 1400 &&
                metrics.ActiveColumns >= 160 &&
                metrics.SpanWidth >= 240 &&
                metrics.SpanWidth <= 650 &&
                metrics.BandRows >= 12 &&
                metrics.BandCenterRatio >= 0.18 &&
                metrics.BandCenterRatio <= 0.58 &&
                metrics.TextComponentCount >= 6 &&
                metrics.TextComponentCount <= 22 &&
                metrics.TextComponentArea >= 5000 &&
                metrics.TextComponentSpan >= 160 &&
                metrics.TextComponentSpan <= 650 &&
                metrics.OutlinedPixels >= 1800 &&
                metrics.OutlinedColumns >= 120;

            var lightBackgroundText =
                metrics.WhitePixels >= 2400 &&
                metrics.WhiteRatio >= 0.045 &&
                metrics.BandScore >= 1200 &&
                metrics.ActiveColumns >= 145 &&
                metrics.SpanWidth >= 200 &&
                metrics.SpanWidth <= 900 &&
                metrics.BandRows >= 10 &&
                metrics.BandCenterRatio >= 0.25 &&
                metrics.BandCenterRatio <= 0.70 &&
                metrics.TextComponentCount >= 4 &&
                metrics.TextComponentCount <= 28 &&
                metrics.TextComponentArea >= 4500 &&
                metrics.TextComponentSpan >= 220 &&
                metrics.TextComponentSpan <= 900 &&
                metrics.OutlinedPixels >= 120 &&
                metrics.OutlinedColumns >= 55;

            return strictOutlinedText || lightBackgroundText;
        }

        private static bool HasLargeMoonGetCelebrationMass(Mat image)
        {
            const int step = 4;
            var sampledWidth = (image.Width + step - 1) / step;
            var sampledHeight = (image.Height + step - 1) / step;
            var rowCounts = new int[sampledHeight];
            var activeColumns = new bool[sampledWidth];
            var whitePixels = 0;

            for (var y = 0; y < image.Height; y += step)
            {
                var sampledY = y / step;
                var rowCount = 0;

                for (var x = 0; x < image.Width; x += step)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    if (!IsMoonGetTextPixel(r, g, b))
                        continue;

                    rowCount++;
                    whitePixels++;
                    activeColumns[x / step] = true;
                }

                rowCounts[sampledY] = rowCount;
            }

            var activeRows = rowCounts.Count(count => count >= sampledWidth * 0.10);
            var strongRows = rowCounts.Count(count => count >= sampledWidth * 0.30);
            var maxRow = rowCounts.Length == 0 ? 0 : rowCounts.Max();
            var span = MeasureBooleanSpan(activeColumns);

            return
                whitePixels >= sampledWidth * sampledHeight * 0.06 &&
                maxRow >= sampledWidth * 0.36 &&
                activeRows >= 12 &&
                strongRows >= 3 &&
                span >= sampledWidth * 0.45;
        }

        private static bool HasLargeMoonGetCelebrationText(MoonGetTextMetrics metrics)
        {
            return
                metrics.WhitePixels >= 90_000 &&
                metrics.WhiteRatio >= 0.08 &&
                metrics.BandScore >= 12_000 &&
                metrics.ActiveColumns >= 600 &&
                metrics.SpanWidth >= 520 &&
                metrics.BandRows >= 18 &&
                metrics.TextComponentCount >= 4 &&
                metrics.TextComponentCount <= 260 &&
                metrics.TextComponentArea >= 4_500 &&
                metrics.TextComponentSpan >= 420 &&
                metrics.OutlinedPixels >= 1_000 &&
                metrics.OutlinedColumns >= 120;
        }

        private static bool HasMoonGetBannerText(Mat image)
        {
            var paleNeutralRatio = MeasurePaleNeutralRatio(image);

            if (HasMoonGetSeparatorLayout(image))
                return true;

            if (paleNeutralRatio > 0.82)
                return false;

            var darkIntegral = BuildDarkIntegral(image, maxValue: 105);
            const int step = 2;
            var sampledWidth = (image.Width + step - 1) / step;
            var sampledHeight = (image.Height + step - 1) / step;
            var rowCounts = new int[sampledHeight];
            var outlinedPixels = 0;
            var activeColumns = new bool[sampledWidth];

            for (var y = 0; y < image.Height; y += step)
            {
                var rowCount = 0;
                var sampledY = y / step;

                for (var x = 0; x < image.Width; x += step)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    if (!IsMoonGetTextPixel(r, g, b))
                        continue;

                    if (!HasNearbyDarkPixel(darkIntegral, image.Width, image.Height, x, y, radius: 3))
                        continue;

                    rowCount++;
                    outlinedPixels++;
                    activeColumns[x / step] = true;
                }

                rowCounts[sampledY] = rowCount;
            }

            var activeColumnCount = activeColumns.Count(x => x);
            var maxRow = rowCounts.Length == 0 ? 0 : rowCounts.Max();
            var titleRows = rowCounts.Count(count => count >= sampledWidth * 0.04);
            var left = activeColumns.Length;
            var right = 0;

            for (var x = 0; x < activeColumns.Length; x++)
            {
                if (!activeColumns[x])
                    continue;

                left = Math.Min(left, x);
                right = Math.Max(right, x + 1);
            }

            var span = right - (left == activeColumns.Length ? right : left);

            return
                outlinedPixels >= 450 &&
                maxRow >= sampledWidth * 0.42 &&
                titleRows >= 9 &&
                activeColumnCount >= sampledWidth * 0.55 &&
                span >= sampledWidth * 0.55;
        }

        private static bool HasMoonGetSeparatorLayout(Mat image)
        {
            const int step = 2;
            var sampledWidth = (image.Width + step - 1) / step;
            var sampledHeight = (image.Height + step - 1) / step;
            var rowCounts = new int[sampledHeight];
            var activeColumnsAboveLine = new bool[sampledWidth];

            for (var y = 0; y < image.Height; y += step)
            {
                var sampledY = y / step;
                var rowCount = 0;

                for (var x = 0; x < image.Width; x += step)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    if (!IsMoonGetTextPixel(r, g, b))
                        continue;

                    rowCount++;
                }

                rowCounts[sampledY] = rowCount;
            }

            var minLineRow = (int)(sampledHeight * 0.35);
            var maxLineRow = (int)(sampledHeight * 0.84);
            var lineThreshold = sampledWidth * 0.45;
            var bestLineStart = -1;
            var bestLineEnd = -1;
            var bestLineScore = 0;
            var runStart = -1;
            var runScore = 0;

            for (var y = minLineRow; y < maxLineRow; y++)
            {
                if (rowCounts[y] >= lineThreshold)
                {
                    if (runStart < 0)
                    {
                        runStart = y;
                        runScore = 0;
                    }

                    runScore += rowCounts[y];
                    continue;
                }

                CommitLineRun(y);
            }

            CommitLineRun(maxLineRow);

            if (bestLineStart < 0)
                return false;

            var lineRows = bestLineEnd - bestLineStart;
            if (lineRows < 2 || lineRows > 24)
                return false;

            var titleRows = 0;
            var titleBottom = Math.Max(0, bestLineStart - 4);
            var titleRowThreshold = sampledWidth * 0.09;
            var titleStrongRowThreshold = sampledWidth * 0.16;
            var strongTitleRows = 0;

            for (var y = 0; y < titleBottom; y++)
            {
                if (rowCounts[y] >= titleRowThreshold)
                    titleRows++;

                if (rowCounts[y] >= titleStrongRowThreshold)
                    strongTitleRows++;

                if (rowCounts[y] < titleRowThreshold)
                    continue;

                var sourceY = y * step;
                for (var x = 0; x < image.Width; x += step)
                {
                    var pixel = image.At<Vec3b>(sourceY, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    if (IsMoonGetTextPixel(r, g, b))
                        activeColumnsAboveLine[x / step] = true;
                }
            }

            var titleColumnSpan = MeasureBooleanSpan(activeColumnsAboveLine);
            var nameRows = 0;
            var nameRowThreshold = sampledWidth * 0.06;

            for (var y = bestLineEnd + 3; y < sampledHeight; y++)
            {
                if (rowCounts[y] >= nameRowThreshold)
                    nameRows++;
            }

            return
                titleRows >= 5 &&
                strongTitleRows >= 2 &&
                titleColumnSpan >= sampledWidth * 0.32 &&
                nameRows >= 1;

            void CommitLineRun(int end)
            {
                if (runStart < 0)
                    return;

                var runLength = end - runStart;
                if (runLength >= 2 &&
                    runLength <= 24 &&
                    HasSeparatorLineContrast(runStart, end) &&
                    runScore > bestLineScore)
                {
                    bestLineStart = runStart;
                    bestLineEnd = end;
                    bestLineScore = runScore;
                }

                runStart = -1;
                runScore = 0;
            }

            bool HasSeparatorLineContrast(int start, int end)
            {
                var lineAverage = 0.0;
                for (var y = start; y < end; y++)
                    lineAverage += rowCounts[y];

                lineAverage /= Math.Max(1, end - start);

                var surroundingTotal = 0;
                var surroundingRows = 0;
                for (var y = Math.Max(0, start - 10); y < Math.Max(0, start - 2); y++)
                {
                    surroundingTotal += rowCounts[y];
                    surroundingRows++;
                }

                for (var y = Math.Min(sampledHeight, end + 2); y < Math.Min(sampledHeight, end + 10); y++)
                {
                    surroundingTotal += rowCounts[y];
                    surroundingRows++;
                }

                var surroundingAverage = surroundingRows == 0
                    ? 0
                    : surroundingTotal / (double)surroundingRows;

                return
                    lineAverage >= sampledWidth * 0.48 &&
                    lineAverage >= surroundingAverage * 1.25 &&
                    lineAverage - surroundingAverage >= sampledWidth * 0.12;
            }
        }

        private static int MeasureBooleanSpan(bool[] values)
        {
            var left = values.Length;
            var right = 0;

            for (var i = 0; i < values.Length; i++)
            {
                if (!values[i])
                    continue;

                left = Math.Min(left, i);
                right = Math.Max(right, i + 1);
            }

            return right - (left == values.Length ? right : left);
        }

        private static double MeasurePaleNeutralRatio(Mat image)
        {
            var palePixels = 0;

            for (var y = 0; y < image.Height; y += 3)
            {
                for (var x = 0; x < image.Width; x += 3)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;
                    var max = Math.Max(r, Math.Max(g, b));
                    var min = Math.Min(r, Math.Min(g, b));

                    if (max >= 170 && min >= 135 && max - min <= 55)
                        palePixels++;
                }
            }

            var sampledWidth = (image.Width + 2) / 3;
            var sampledHeight = (image.Height + 2) / 3;
            return palePixels / (double)Math.Max(1, sampledWidth * sampledHeight);
        }

        private static MoonGetTextMetrics MeasureMoonGetText(Mat image)
        {
            using var mask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.Black);
            var darkIntegral = BuildDarkIntegral(image);
            var rowCounts = new int[image.Height];
            var outlinedColumns = new bool[image.Width];
            var whitePixels = 0;
            var outlinedPixels = 0;

            for (var y = 0; y < image.Height; y++)
            {
                var rowCount = 0;
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    if (!IsMoonGetTextPixel(r, g, b))
                        continue;

                    mask.Set(y, x, 255);
                    rowCount++;
                    whitePixels++;

                    if (HasNearbyDarkPixel(darkIntegral, image.Width, image.Height, x, y))
                    {
                        outlinedPixels++;
                        outlinedColumns[x] = true;
                    }
                }

                rowCounts[y] = rowCount;
            }

            var rowThreshold = Math.Max(34.0, image.Width * 0.04);
            var bandTop = 0;
            var bandBottom = 0;
            var bestBandScore = 0.0;

            for (var start = 0; start < image.Height; start++)
            {
                var score = 0.0;
                for (var end = start; end < Math.Min(image.Height, start + 42); end++)
                {
                    score += Math.Max(0, rowCounts[end] - rowThreshold);
                    if (end - start + 1 >= 8 && score > bestBandScore)
                    {
                        bestBandScore = score;
                        bandTop = start;
                        bandBottom = end + 1;
                    }
                }
            }

            var activeColumns = 0;
            var left = image.Width;
            var right = 0;

            for (var x = 0; x < image.Width; x++)
            {
                var count = 0;
                for (var y = bandTop; y < bandBottom; y++)
                {
                    if (mask.At<byte>(y, x) > 0)
                        count++;
                }

                if (count >= 2)
                {
                    activeColumns++;
                    left = Math.Min(left, x);
                    right = Math.Max(right, x + 1);
                }
            }

            var (componentCount, componentArea, componentSpan) = MeasureMoonGetTextComponents(mask);
            var spanWidth = right - (left == image.Width ? right : left);
            var center = bandBottom <= bandTop
                ? 0
                : ((bandTop + bandBottom) / 2.0) / Math.Max(1, image.Height);

            return new MoonGetTextMetrics(
                whitePixels,
                whitePixels / (double)Math.Max(1, image.Width * image.Height),
                bestBandScore,
                bandBottom - bandTop,
                center,
                activeColumns,
                spanWidth,
                componentCount,
                componentArea,
                componentSpan,
                outlinedPixels,
                outlinedColumns.Count(x => x));
        }

        private static (int Count, int Area, int Span) MeasureMoonGetTextComponents(Mat mask)
        {
            using var labels = new Mat();
            using var stats = new Mat();
            using var centroids = new Mat();
            var componentCount = Cv2.ConnectedComponentsWithStats(mask, labels, stats, centroids);

            var count = 0;
            var areaSum = 0;
            var left = mask.Width;
            var right = 0;

            for (var i = 1; i < componentCount; i++)
            {
                var x = stats.Get<int>(i, (int)ConnectedComponentsTypes.Left);
                var y = stats.Get<int>(i, (int)ConnectedComponentsTypes.Top);
                var width = stats.Get<int>(i, (int)ConnectedComponentsTypes.Width);
                var height = stats.Get<int>(i, (int)ConnectedComponentsTypes.Height);
                var area = stats.Get<int>(i, (int)ConnectedComponentsTypes.Area);
                var fill = area / (double)Math.Max(1, width * height);

                if (area < 20 || area > 3500)
                    continue;

                if (width < 3 || width > 105 || height < 5 || height > 55)
                    continue;

                if (fill < 0.08 || fill > 0.98)
                    continue;

                if (y > mask.Height * 0.9)
                    continue;

                count++;
                areaSum += area;
                left = Math.Min(left, x);
                right = Math.Max(right, x + width);
            }

            return (count, areaSum, right - (left == mask.Width ? right : left));
        }

        private static int[,] BuildDarkIntegral(Mat image, int maxValue = 95)
        {
            var integral = new int[image.Height + 1, image.Width + 1];

            for (var y = 0; y < image.Height; y++)
            {
                var rowSum = 0;
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var max = Math.Max(pixel.Item2, Math.Max(pixel.Item1, pixel.Item0));
                    if (max <= maxValue)
                        rowSum++;

                    integral[y + 1, x + 1] = integral[y, x + 1] + rowSum;
                }
            }

            return integral;
        }

        private static bool HasNearbyDarkPixel(int[,] darkIntegral, int width, int height, int x, int y, int radius = 2)
        {
            var minX = Math.Max(0, x - radius);
            var maxX = Math.Min(width - 1, x + radius);
            var minY = Math.Max(0, y - radius);
            var maxY = Math.Min(height - 1, y + radius);
            var count =
                darkIntegral[maxY + 1, maxX + 1] -
                darkIntegral[minY, maxX + 1] -
                darkIntegral[maxY + 1, minX] +
                darkIntegral[minY, minX];

            return count > 0;
        }

        private static bool IsMoonGetTextPixel(int r, int g, int b)
        {
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            return max >= 145 && min >= 115 && max - min <= 75;
        }

        private readonly record struct MoonGetTextMetrics(
            int WhitePixels,
            double WhiteRatio,
            double BandScore,
            int BandRows,
            double BandCenterRatio,
            int ActiveColumns,
            int SpanWidth,
            int TextComponentCount,
            int TextComponentArea,
            int TextComponentSpan,
            int OutlinedPixels,
            int OutlinedColumns);

        private readonly record struct BrightYellowTextMetrics(
            int BrightPixels,
            double BrightRatio,
            double BandScore,
            int BandRows,
            double BandCenterRatio,
            int ActiveColumns,
            int SpanWidth,
            int LongestColumnRun,
            int FragmentedColumns,
            int DarkSupportedPixels,
            int DarkSupportedColumns);

        public static bool HasStoryMoonText(Mat image)
        {
            if (image.Empty() || image.Channels() < 3)
                return false;

            var redRatio = MeasureRedCelebrationRatio(image);
            if (redRatio < 0.30)
                return false;

            var metrics = MeasureMoonGetText(image);

            return
                metrics.WhitePixels >= 20000 &&
                metrics.WhiteRatio >= 0.12 &&
                metrics.BandScore >= 10000 &&
                metrics.ActiveColumns >= 390 &&
                metrics.SpanWidth >= 450 &&
                metrics.BandRows >= 10 &&
                metrics.BandCenterRatio >= 0.15 &&
                metrics.BandCenterRatio <= 0.55 &&
                metrics.OutlinedPixels >= 300 &&
                metrics.OutlinedColumns >= 100;
        }

        private static double MeasureRedCelebrationRatio(Mat image)
        {
            var redPixels = 0;

            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    if (r >= 145 && g <= 105 && b <= 125 && r >= g + 45 && r >= b + 45)
                        redPixels++;
                }
            }

            return redPixels / (double)Math.Max(1, image.Width * image.Height);
        }

        public static bool HasMoonTextHeuristic(Mat image)
        {
            using var gray = image.CvtColor(ColorConversionCodes.BGR2GRAY);
            using var blurred = gray.GaussianBlur(new Size(3, 3), 0);
            using var edges = blurred.Canny(80, 160);

            int width = edges.Cols;
            int height = edges.Rows;

            int totalPixels = width * height;
            int edgePixels = Cv2.CountNonZero(edges);

            double density = (double)edgePixels / totalPixels;

            // Too little structure
            if (density < 0.02)
                return false;

            // ----------------------------------------
            // 1. Row projection (vertical structure)
            // ----------------------------------------
            int[] rowCounts = new int[height];

            for (int y = 0; y < height; y++)
            {
                int count = 0;
                for (int x = 0; x < width; x++)
                {
                    if (edges.At<byte>(y, x) > 0)
                        count++;
                }
                rowCounts[y] = count;
            }

            // ----------------------------------------
            // 2. Find main band (largest continuous run)
            // ----------------------------------------
            int longestRun = 0;
            int currentRun = 0;
            int start = 0;
            int bestStart = 0;

            for (int y = 0; y < height; y++)
            {
                bool active = rowCounts[y] > width * 0.1; // 10% of row

                if (active)
                {
                    if (currentRun == 0)
                        start = y;

                    currentRun++;

                    if (currentRun > longestRun)
                    {
                        longestRun = currentRun;
                        bestStart = start;
                    }
                }
                else
                {
                    currentRun = 0;
                }
            }

            // No strong band
            if (longestRun < height * 0.4)
                return false;

            int bandCenter = bestStart + longestRun / 2;

            // ----------------------------------------
            // 3. Ensure band is vertically centered
            // ----------------------------------------
            double centerOffset = Math.Abs(bandCenter - height / 2.0) / height;

            if (centerOffset > 0.2) // too far from center
                return false;

            // ----------------------------------------
            // 4. Column spread (handles short text)
            // ----------------------------------------
            int activeCols = 0;

            for (int x = 0; x < width; x++)
            {
                int count = 0;

                for (int y = bestStart; y < bestStart + longestRun; y++)
                {
                    if (edges.At<byte>(y, x) > 0)
                        count++;
                }

                if (count > longestRun * 0.1)
                    activeCols++;
            }

            double colRatio = (double)activeCols / width;

            // Too narrow, probably noise
            if (colRatio < 0.1)
                return false;

            return true;
        }

        //public static bool MoonGet(Mat image)
        //{
        //    using var gray = image.CvtColor(ColorConversionCodes.BGR2GRAY);

        //    int h = gray.Rows;
        //    int w = gray.Cols;

        //    // Check specific pixels (in "YOU GOT A MOON" area)
        //    byte p1 = gray.At<byte>(h / 2, w / 4);
        //    byte p2 = gray.At<byte>(h / 2, w / 2);
        //    byte p3 = gray.At<byte>(h / 2, 3 * w / 4);

        //    int bright =
        //        (p1 > 200 ? 1 : 0) +
        //        (p2 > 200 ? 1 : 0) +
        //        (p3 > 200 ? 1 : 0);

        //    return bright >= 2;
        //}
    }
}
