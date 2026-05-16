using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public class TextDetection
    {
        //image.SaveImage("[removed]");

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
            return false; // #TODO fix

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