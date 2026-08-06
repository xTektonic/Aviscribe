using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public static class TextDetection
    {
        //image.SaveImage("[removed]");

        public static bool HasTalkatooText(Mat image)
        {
            return TalkatooStaticGate.Analyze(image).Present;
        }

        public static bool HasMoonText(Mat image)
        {
            if (image.Empty() || image.Channels() < 3)
                return false;

            if (image.Height > 100)
            {
                return AnalyzeMoonGetSeparatorLayout(image).Detected;
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

        public static MoonGetSeparatorMetrics AnalyzeMoonGetSeparatorLayout(Mat image)
        {
            const int step = 2;
            var width = image.Width;
            var height = image.Height;
            var sampledWidth = (width + step - 1) / step;
            var sampledHeight = (height + step - 1) / step;
            var rowCounts = new int[sampledHeight];
            var strongWhiteRowCounts = new int[sampledHeight];
            var activeColumnsAboveLine = new bool[sampledWidth];

            for (var y = 0; y < height; y += step)
            {
                var sampledY = y / step;
                var rowCount = 0;
                var strongWhiteRowCount = 0;

                for (var x = 0; x < width; x += step)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    if (!IsMoonGetTextPixel(r, g, b))
                        continue;

                    rowCount++;

                    if (IsStrongMoonGetWhitePixel(r, g, b))
                        strongWhiteRowCount++;
                }

                rowCounts[sampledY] = rowCount;
                strongWhiteRowCounts[sampledY] = strongWhiteRowCount;
            }

            var minLineRow = (int)(sampledHeight * 0.48);
            var maxLineRow = (int)(sampledHeight * 0.82);
            var fullPaleRows = rowCounts.Count(count => count >= sampledWidth * 0.96);
            var averagePaleRow = rowCounts.Average();
            var useStrongWhiteLayout = fullPaleRows > 24 || averagePaleRow >= sampledWidth * 0.42;
            var separatorRows = useStrongWhiteLayout
                ? strongWhiteRowCounts
                : rowCounts;
            var lineThreshold = sampledWidth * (useStrongWhiteLayout ? 0.54 : 0.72);

            var bestLineStart = -1;
            var bestLineEnd = -1;
            var bestLineScore = 0;
            var bestLineAverage = 0.0;
            var bestSurroundingAverage = 0.0;
            var runStart = -1;
            var runScore = 0;

            for (var y = minLineRow; y < maxLineRow; y++)
            {
                if (separatorRows[y] >= lineThreshold)
                {
                    if (runStart < 0)
                    {
                        runStart = y;
                        runScore = 0;
                    }

                    runScore += separatorRows[y];
                    continue;
                }

                CommitLineRun(y);
            }

            CommitLineRun(maxLineRow);

            if (bestLineStart < 0)
            {
                return new MoonGetSeparatorMetrics(
                    false,
                    sampledWidth,
                    sampledHeight,
                    -1,
                    -1,
                    0,
                    0,
                    0,
                    fullPaleRows,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0);
            }

            var lineRows = bestLineEnd - bestLineStart;
            if (lineRows < 2 || lineRows > 24)
            {
                return new MoonGetSeparatorMetrics(
                    false,
                    sampledWidth,
                    sampledHeight,
                    bestLineStart,
                    bestLineEnd,
                    lineRows,
                    bestLineAverage,
                    bestSurroundingAverage,
                    fullPaleRows,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0);
            }

            var titleRows = 0;
            var titleBottom = Math.Max(0, bestLineStart - 4);
            var titleRowThreshold = sampledWidth * (useStrongWhiteLayout ? 0.045 : 0.09);
            var titleStrongRowThreshold = sampledWidth * (useStrongWhiteLayout ? 0.075 : 0.16);
            var bannerWhiteRowThreshold = sampledWidth * 0.28;
            var fadedBannerRowThreshold = sampledWidth * (useStrongWhiteLayout ? 0.16 : 0.32);
            var topPanelRowThreshold = sampledWidth * (useStrongWhiteLayout ? 0.72 : 0.55);
            var strongTitleRows = 0;
            var strongBannerRows = 0;
            var fadedBannerRows = 0;
            var topPanelRows = 0;
            var topPanelBottom = Math.Min(titleBottom, Math.Max(1, (int)(sampledHeight * 0.18)));

            for (var y = 0; y < titleBottom; y++)
            {
                var layoutRowCount = useStrongWhiteLayout
                    ? strongWhiteRowCounts[y]
                    : rowCounts[y];

                if (layoutRowCount >= titleRowThreshold)
                    titleRows++;

                if (layoutRowCount >= titleStrongRowThreshold)
                    strongTitleRows++;

                if (strongWhiteRowCounts[y] >= bannerWhiteRowThreshold)
                    strongBannerRows++;

                if (layoutRowCount >= fadedBannerRowThreshold)
                    fadedBannerRows++;

                if (y < topPanelBottom && layoutRowCount >= topPanelRowThreshold)
                    topPanelRows++;

                if (layoutRowCount < titleRowThreshold)
                    continue;

                var sourceY = y * step;
                for (var x = 0; x < width; x += step)
                {
                    var pixel = image.At<Vec3b>(sourceY, x);
                    var b = pixel.Item0;
                    var g = pixel.Item1;
                    var r = pixel.Item2;

                    var activeTitlePixel = useStrongWhiteLayout
                        ? IsStrongMoonGetWhitePixel(r, g, b)
                        : IsMoonGetTextPixel(r, g, b);

                    if (activeTitlePixel)
                        activeColumnsAboveLine[x / step] = true;
                }
            }

            var titleColumnSpan = MeasureBooleanSpan(activeColumnsAboveLine);
            var nameRows = 0;
            var nameRowThreshold = sampledWidth * (useStrongWhiteLayout ? 0.035 : 0.06);
            var hasMoonGetBanner = (strongBannerRows >= 10 && topPanelRows <= 12) ||
                (fadedBannerRows >= (useStrongWhiteLayout ? 10 : 18) && topPanelRows <= (useStrongWhiteLayout ? 12 : 6));
            var sceneryTitleRows = rowCounts
                .Take(titleBottom)
                .Select((count, y) => useStrongWhiteLayout ? strongWhiteRowCounts[y] : count)
                .Count(count => count >= sampledWidth * (useStrongWhiteLayout ? 0.36 : 0.48));

            for (var y = bestLineEnd + 3; y < sampledHeight; y++)
            {
                var layoutRowCount = useStrongWhiteLayout
                    ? strongWhiteRowCounts[y]
                    : rowCounts[y];

                if (layoutRowCount >= nameRowThreshold)
                    nameRows++;
            }

            var detected =
                lineRows >= 6 &&
                (!useStrongWhiteLayout || bestLineAverage >= sampledWidth * 0.82) &&
                titleRows >= 5 &&
                strongTitleRows >= 2 &&
                hasMoonGetBanner &&
                sceneryTitleRows <= Math.Max(useStrongWhiteLayout ? 14 : 10, strongTitleRows + 8) &&
                titleColumnSpan >= sampledWidth * (useStrongWhiteLayout ? 0.78 : 0.32) &&
                nameRows >= 1;

            return new MoonGetSeparatorMetrics(
                detected,
                sampledWidth,
                sampledHeight,
                bestLineStart,
                bestLineEnd,
                lineRows,
                bestLineAverage,
                bestSurroundingAverage,
                fullPaleRows,
                titleRows,
                strongTitleRows,
                strongBannerRows,
                fadedBannerRows,
                topPanelRows,
                sceneryTitleRows,
                titleColumnSpan,
                nameRows);

            void CommitLineRun(int end)
            {
                if (runStart < 0)
                    return;

                var runLength = end - runStart;
                var contrast = MeasureSeparatorLineContrast(runStart, end);
                if (runLength >= 2 &&
                    runLength <= 24 &&
                    contrast.Passed &&
                    runScore > bestLineScore)
                {
                    bestLineStart = runStart;
                    bestLineEnd = end;
                    bestLineScore = runScore;
                    bestLineAverage = contrast.LineAverage;
                    bestSurroundingAverage = contrast.SurroundingAverage;
                }

                runStart = -1;
                runScore = 0;
            }

            (bool Passed, double LineAverage, double SurroundingAverage) MeasureSeparatorLineContrast(int start, int end)
            {
                var lineAverage = 0.0;
                for (var y = start; y < end; y++)
                    lineAverage += separatorRows[y];

                lineAverage /= Math.Max(1, end - start);

                var surroundingTotal = 0;
                var surroundingRows = 0;
                for (var y = Math.Max(0, start - 10); y < Math.Max(0, start - 2); y++)
                {
                    surroundingTotal += separatorRows[y];
                    surroundingRows++;
                }

                for (var y = Math.Min(sampledHeight, end + 2); y < Math.Min(sampledHeight, end + 10); y++)
                {
                    surroundingTotal += separatorRows[y];
                    surroundingRows++;
                }

                var surroundingAverage = surroundingRows == 0
                    ? 0
                    : surroundingTotal / (double)surroundingRows;

                var passed =
                    lineAverage >= lineThreshold &&
                    surroundingAverage <= sampledWidth * (useStrongWhiteLayout ? 0.34 : 0.45) &&
                    lineAverage >= surroundingAverage * 1.25 &&
                    lineAverage - surroundingAverage >= sampledWidth * (useStrongWhiteLayout ? 0.10 : 0.12);

                return (passed, lineAverage, surroundingAverage);
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

        private static MoonGetTextMetrics MeasureMoonGetText(Mat image)
        {
            var width = image.Width;
            var height = image.Height;
            using var mask = new Mat(image.Size(), MatType.CV_8UC1, Scalar.Black);
            var darkIntegral = BuildDarkIntegral(image);
            var rowCounts = new int[height];
            var outlinedColumns = new bool[width];
            var whitePixels = 0;
            var outlinedPixels = 0;

            for (var y = 0; y < height; y++)
            {
                var rowCount = 0;
                for (var x = 0; x < width; x++)
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

                    if (HasNearbyDarkPixel(darkIntegral, width, height, x, y))
                    {
                        outlinedPixels++;
                        outlinedColumns[x] = true;
                    }
                }

                rowCounts[y] = rowCount;
            }

            var rowThreshold = Math.Max(34.0, width * 0.04);
            var bandTop = 0;
            var bandBottom = 0;
            var bestBandScore = 0.0;

            for (var start = 0; start < height; start++)
            {
                var score = 0.0;
                for (var end = start; end < Math.Min(height, start + 42); end++)
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
            var left = width;
            var right = 0;

            for (var x = 0; x < width; x++)
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
            var spanWidth = right - (left == width ? right : left);
            var center = bandBottom <= bandTop
                ? 0
                : ((bandTop + bandBottom) / 2.0) / Math.Max(1, height);

            return new MoonGetTextMetrics(
                whitePixels,
                whitePixels / (double)Math.Max(1, width * height),
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
            var width = image.Width;
            var height = image.Height;
            var integral = new int[height + 1, width + 1];

            for (var y = 0; y < height; y++)
            {
                var rowSum = 0;
                for (var x = 0; x < width; x++)
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

        private static bool IsStrongMoonGetWhitePixel(int r, int g, int b)
        {
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            return max >= 225 && min >= 205 && max - min <= 40;
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

        public readonly record struct MoonGetSeparatorMetrics(
            bool Detected,
            int SampledWidth,
            int SampledHeight,
            int LineStart,
            int LineEnd,
            int LineRows,
            double LineAverage,
            double SurroundingAverage,
            int FullPaleRows,
            int TitleRows,
            int StrongTitleRows,
            int StrongBannerRows,
            int FadedBannerRows,
            int TopPanelRows,
            int SceneryTitleRows,
            int TitleColumnSpan,
            int NameRows);

        private const int StoryMoonBackgroundPatchSize = 24;
        private const double StoryMoonBackgroundTolerance = 30.0;
        private const int RequiredStoryMoonBackgroundMatches = 3;

        // These tiles are relative to the existing StoryMoon OCR crop. They avoid
        // the bottom-left splits and variable character/name artwork. Expected
        // values are RGB mean and population standard deviation from 37 labeled
        // collection screens at the normalized 1920-by-1080 reference layout.
        private static readonly StoryMoonBackgroundPatch[] StoryMoonBackgroundPatches =
        [
            new(
                new Rect(1030, 56, StoryMoonBackgroundPatchSize, StoryMoonBackgroundPatchSize),
                new StoryMoonColorStatistics(225.1, 0.7, 2.1, 1.3, 0.5, 1.2)),
            new(
                new Rect(1074, 4, StoryMoonBackgroundPatchSize, StoryMoonBackgroundPatchSize),
                new StoryMoonColorStatistics(224.9, 0.9, 2.7, 2.1, 2.3, 3.8)),
            new(
                new Rect(946, 8, StoryMoonBackgroundPatchSize, StoryMoonBackgroundPatchSize),
                new StoryMoonColorStatistics(224.6, 4.1, 9.4, 8.1, 10.4, 15.2)),
            new(
                new Rect(250, 124, StoryMoonBackgroundPatchSize, StoryMoonBackgroundPatchSize),
                new StoryMoonColorStatistics(225.3, 5.0, 10.7, 9.0, 11.5, 16.4))
        ];

        public static bool HasStoryMoonText(Mat image)
        {
            if (image.Empty() || image.Channels() < 3)
                return false;

            var matches = 0;
            for (var index = 0; index < StoryMoonBackgroundPatches.Length; index++)
            {
                var remaining = StoryMoonBackgroundPatches.Length - index;
                if (matches + remaining < RequiredStoryMoonBackgroundMatches)
                    return false;

                var patch = StoryMoonBackgroundPatches[index];
                if (!Contains(image, patch.Bounds))
                    return false;

                var measured = MeasureStoryMoonColorStatistics(image, patch.Bounds);
                if (MatchesStoryMoonBackground(measured, patch.Expected))
                    matches++;
            }

            return matches >= RequiredStoryMoonBackgroundMatches;
        }

        private static bool Contains(Mat image, Rect bounds)
        {
            return
                bounds.X >= 0 &&
                bounds.Y >= 0 &&
                bounds.Right <= image.Width &&
                bounds.Bottom <= image.Height;
        }

        private static StoryMoonColorStatistics MeasureStoryMoonColorStatistics(
            Mat image,
            Rect bounds)
        {
            var pixelCount = bounds.Width * bounds.Height;
            var redSum = 0.0;
            var greenSum = 0.0;
            var blueSum = 0.0;
            var redSquaredSum = 0.0;
            var greenSquaredSum = 0.0;
            var blueSquaredSum = 0.0;

            for (var y = bounds.Top; y < bounds.Bottom; y++)
            {
                for (var x = bounds.Left; x < bounds.Right; x++)
                {
                    var pixel = image.At<Vec3b>(y, x);
                    var blue = (double)pixel.Item0;
                    var green = (double)pixel.Item1;
                    var red = (double)pixel.Item2;

                    redSum += red;
                    greenSum += green;
                    blueSum += blue;
                    redSquaredSum += red * red;
                    greenSquaredSum += green * green;
                    blueSquaredSum += blue * blue;
                }
            }

            var redMean = redSum / pixelCount;
            var greenMean = greenSum / pixelCount;
            var blueMean = blueSum / pixelCount;

            return new StoryMoonColorStatistics(
                redMean,
                greenMean,
                blueMean,
                StandardDeviation(redSquaredSum, redMean, pixelCount),
                StandardDeviation(greenSquaredSum, greenMean, pixelCount),
                StandardDeviation(blueSquaredSum, blueMean, pixelCount));
        }

        private static double StandardDeviation(
            double squaredSum,
            double mean,
            int pixelCount)
        {
            var variance = squaredSum / pixelCount - mean * mean;
            return Math.Sqrt(Math.Max(0, variance));
        }

        private static bool MatchesStoryMoonBackground(
            StoryMoonColorStatistics measured,
            StoryMoonColorStatistics expected)
        {
            return
                Math.Abs(measured.RedMean - expected.RedMean) <= StoryMoonBackgroundTolerance &&
                Math.Abs(measured.GreenMean - expected.GreenMean) <= StoryMoonBackgroundTolerance &&
                Math.Abs(measured.BlueMean - expected.BlueMean) <= StoryMoonBackgroundTolerance &&
                Math.Abs(measured.RedStandardDeviation - expected.RedStandardDeviation) <= StoryMoonBackgroundTolerance &&
                Math.Abs(measured.GreenStandardDeviation - expected.GreenStandardDeviation) <= StoryMoonBackgroundTolerance &&
                Math.Abs(measured.BlueStandardDeviation - expected.BlueStandardDeviation) <= StoryMoonBackgroundTolerance;
        }

        private readonly record struct StoryMoonBackgroundPatch(
            Rect Bounds,
            StoryMoonColorStatistics Expected);

        private readonly record struct StoryMoonColorStatistics(
            double RedMean,
            double GreenMean,
            double BlueMean,
            double RedStandardDeviation,
            double GreenStandardDeviation,
            double BlueStandardDeviation);
    }
}
