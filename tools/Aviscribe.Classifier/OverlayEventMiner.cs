using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class OverlayEventMiner
    {
        private const int SignatureColumns = 44;
        private const int SignatureRows = 12;

        private static readonly Rect OverlayBounds = new(520, 0, 880, 135);
        private static readonly Rect TalkatooBounds = new(666, 862, 649, 48);
        private static readonly Rect MoonGetBounds = new(320, 600, 1250, 250);

        public static void Mine(
            string videoPath,
            string outputDir,
            int strideFrames,
            int maxFrames,
            int minGapFrames)
        {
            if (strideFrames <= 0)
                throw new ArgumentOutOfRangeException(nameof(strideFrames), "Stride must be positive.");

            Directory.CreateDirectory(outputDir);

            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            using var frame = new Mat();
            OverlaySignature? stableSignature = null;
            OverlaySignature? candidateSignature = null;
            var candidateSamples = 0;
            var lastEventFrame = -minGapFrames;
            var events = new List<OverlayEvent>();

            var frameIndex = 0;
            while (capture.Read(frame) && !frame.Empty())
            {
                frameIndex++;
                if (maxFrames > 0 && frameIndex > maxFrames)
                    break;

                if (frameIndex % strideFrames != 0)
                    continue;

                using var crop = new Mat(frame, OverlayBounds);
                using var mask = CreateOverlayTextMask(crop);
                var signature = MeasureSignature(mask);
                var active = signature.ActivePixels >= 180 && signature.Lines.Count > 0;

                if (!active)
                    continue;

                if (candidateSignature is { } candidate &&
                    signature.Distance(candidate) <= 10)
                {
                    candidateSamples++;
                }
                else
                {
                    candidateSignature = signature;
                    candidateSamples = 1;
                }

                if (candidateSamples < 2)
                    continue;

                var changedPixels = stableSignature is { } stable
                    ? signature.Distance(stable)
                    : 0;

                if (stableSignature != null &&
                    changedPixels >= 24 &&
                    frameIndex - lastEventFrame >= minGapFrames)
                {
                    var talkatoo = Detect(frame, OcrRegionType.Talkatoo, TalkatooBounds);
                    var moonGet = Detect(frame, OcrRegionType.MoonGet, MoonGetBounds);
                    var evt = new OverlayEvent(frameIndex, signature.ActivePixels, changedPixels, talkatoo, moonGet);
                    events.Add(evt);
                    lastEventFrame = frameIndex;
                    WriteEventImages(outputDir, frame, crop, mask, evt);
                }

                stableSignature = signature;
            }

            WriteCsv(outputDir, events);

            Console.WriteLine(
                $"Scanned {frameIndex} frames, found {events.Count} overlay changes, saved to {outputDir}");
            foreach (var evt in events.Take(80))
            {
                Console.WriteLine(
                    $"{evt.Frame:D7}: changed {evt.ChangedPixels}, active {evt.ActivePixels}, " +
                    $"talkatoo={evt.TalkatooDetected}, moonget={evt.MoonGetDetected}");
            }
        }

        public static void AuditCoverage(
            string videoPath,
            string outputDir,
            int strideFrames,
            int maxFrames,
            int minGapFrames,
            int windowFrames)
        {
            Directory.CreateDirectory(outputDir);
            var events = MineEvents(videoPath, strideFrames, maxFrames, minGapFrames);

            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            using var frame = new Mat();
            var misses = new List<OverlayEventCoverage>();
            var results = new List<OverlayEventCoverage>();

            foreach (var evt in events)
            {
                var talkatooFrame = FindDetection(capture, frame, evt.Frame, windowFrames, OcrRegionType.Talkatoo, TalkatooBounds);
                var moonGetFrame = FindDetection(capture, frame, evt.Frame, windowFrames, OcrRegionType.MoonGet, MoonGetBounds);
                var coverage = new OverlayEventCoverage(evt, talkatooFrame, moonGetFrame);
                results.Add(coverage);

                if (coverage.Covered)
                    continue;

                misses.Add(coverage);
                WriteMissImages(capture, frame, outputDir, coverage);
            }

            WriteCoverageCsv(outputDir, results);
            Console.WriteLine($"Overlay event coverage: {results.Count - misses.Count}/{results.Count} covered.");

            foreach (var miss in misses.Take(80))
            {
                Console.WriteLine(
                    $"MISS {miss.Event.Frame:D7}: changed {miss.Event.ChangedPixels}, active {miss.Event.ActivePixels}");
            }

            if (misses.Count > 0)
                throw new InvalidOperationException($"{misses.Count} overlay event(s) had no nearby Talkatoo/MoonGet detection.");
        }

        private static List<OverlayEvent> MineEvents(
            string videoPath,
            int strideFrames,
            int maxFrames,
            int minGapFrames)
        {
            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            using var frame = new Mat();
            var frameIndex = 0;
            OverlaySignature? stableSignature = null;
            OverlaySignature? candidateSignature = null;
            var candidateSamples = 0;
            var lastEventFrame = -minGapFrames;
            var events = new List<OverlayEvent>();

            while (capture.Read(frame) && !frame.Empty())
            {
                frameIndex++;
                if (maxFrames > 0 && frameIndex > maxFrames)
                    break;

                if (frameIndex % strideFrames != 0)
                    continue;

                using var crop = new Mat(frame, OverlayBounds);
                using var mask = CreateOverlayTextMask(crop);
                var signature = MeasureSignature(mask);
                var active = signature.ActivePixels >= 180 && signature.Lines.Count > 0;

                if (!active)
                    continue;

                if (candidateSignature is { } candidate &&
                    signature.Distance(candidate) <= 10)
                {
                    candidateSamples++;
                }
                else
                {
                    candidateSignature = signature;
                    candidateSamples = 1;
                }

                if (candidateSamples < 2)
                    continue;

                var changedPixels = stableSignature is { } stable
                    ? signature.Distance(stable)
                    : 0;

                if (stableSignature != null &&
                    changedPixels >= 24 &&
                    frameIndex - lastEventFrame >= minGapFrames)
                {
                    events.Add(new OverlayEvent(frameIndex, signature.ActivePixels, changedPixels, false, false));
                    lastEventFrame = frameIndex;
                }

                stableSignature = signature;
            }

            return events;
        }

        private static bool Detect(Mat frame, OcrRegionType regionType, Rect bounds)
        {
            var detector = new HeuristicTextPresenceDetector();
            using var crop = new Mat(frame, bounds);
            return detector.Detect(regionType, crop).Present;
        }

        private static int? FindDetection(
            VideoCapture capture,
            Mat frame,
            int centerFrame,
            int windowFrames,
            OcrRegionType regionType,
            Rect bounds)
        {
            var detector = new HeuristicTextPresenceDetector();
            var startFrame = Math.Max(1, centerFrame - windowFrames);
            var endFrame = centerFrame + windowFrames;
            capture.Set(VideoCaptureProperties.PosFrames, startFrame);

            for (var frameIndex = startFrame; frameIndex <= endFrame; frameIndex++)
            {
                if (!capture.Read(frame) || frame.Empty())
                    break;

                if ((frameIndex - startFrame) % 2 != 0)
                    continue;

                using var crop = new Mat(frame, bounds);
                if (detector.Detect(regionType, crop).Present)
                    return frameIndex;
            }

            return null;
        }

        private static Mat CreateOverlayTextMask(Mat image)
        {
            using var hsv = new Mat();
            using var paleMask = new Mat();
            using var gray = new Mat();
            using var darkMask = new Mat();
            using var darkSupport = new Mat();
            var mask = new Mat();

            Cv2.CvtColor(image, hsv, ColorConversionCodes.BGR2HSV);
            Cv2.InRange(hsv, new Scalar(0, 0, 180), new Scalar(180, 90, 255), paleMask);

            Cv2.CvtColor(image, gray, ColorConversionCodes.BGR2GRAY);
            Cv2.Threshold(gray, darkMask, 80, 255, ThresholdTypes.BinaryInv);

            using var darkKernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(5, 5));
            Cv2.Dilate(darkMask, darkSupport, darkKernel);
            Cv2.BitwiseAnd(paleMask, darkSupport, mask);

            using var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(2, 2));
            Cv2.MorphologyEx(mask, mask, MorphTypes.Open, kernel);
            return mask;
        }

        private static OverlaySignature MeasureSignature(Mat mask)
        {
            var rowCounts = new int[mask.Height];
            var activePixels = 0;

            for (var y = 0; y < mask.Height; y++)
            {
                var rowCount = 0;
                for (var x = 0; x < mask.Width; x++)
                {
                    if (mask.At<byte>(y, x) == 0)
                        continue;

                    rowCount++;
                    activePixels++;
                }

                rowCounts[y] = rowCount;
            }

            var lines = new List<OverlayLine>();
            var rowThreshold = Math.Max(20, mask.Width * 0.025);
            var start = -1;

            for (var y = 0; y <= mask.Height; y++)
            {
                var active = y < mask.Height && rowCounts[y] >= rowThreshold;
                if (active && start < 0)
                {
                    start = y;
                    continue;
                }

                if (active || start < 0)
                    continue;

                AddLine(start, y);
                start = -1;
            }

            return new OverlaySignature(activePixels, lines, CreateSignatureCells(mask));

            void AddLine(int top, int bottom)
            {
                if (bottom - top < 8 || bottom - top > 42)
                    return;

                var left = mask.Width;
                var right = 0;
                var pixels = 0;

                for (var y = top; y < bottom; y++)
                {
                    for (var x = 0; x < mask.Width; x++)
                    {
                        if (mask.At<byte>(y, x) == 0)
                            continue;

                        pixels++;
                        left = Math.Min(left, x);
                        right = Math.Max(right, x + 1);
                    }
                }

                var width = right - (left == mask.Width ? right : left);
                if (width < 100 || pixels < 160)
                    return;

                lines.Add(new OverlayLine(
                    top / 6,
                    bottom / 6,
                    (left == mask.Width ? 0 : left) / 12,
                    right / 12,
                    pixels / 120));
            }
        }

        private static byte[] CreateSignatureCells(Mat mask)
        {
            var cells = new byte[SignatureColumns * SignatureRows];

            for (var row = 0; row < SignatureRows; row++)
            {
                var y0 = row * mask.Height / SignatureRows;
                var y1 = (row + 1) * mask.Height / SignatureRows;

                for (var col = 0; col < SignatureColumns; col++)
                {
                    var x0 = col * mask.Width / SignatureColumns;
                    var x1 = (col + 1) * mask.Width / SignatureColumns;
                    var count = 0;

                    for (var y = y0; y < y1; y++)
                    {
                        for (var x = x0; x < x1; x++)
                        {
                            if (mask.At<byte>(y, x) != 0)
                                count++;
                        }
                    }

                    cells[row * SignatureColumns + col] = count >= 3 ? (byte)1 : (byte)0;
                }
            }

            return cells;
        }

        private readonly record struct OverlayLine(
            int Top,
            int Bottom,
            int Left,
            int Right,
            int PixelBucket);

        private readonly record struct OverlaySignature(
            int ActivePixels,
            IReadOnlyList<OverlayLine> Lines,
            IReadOnlyList<byte> Cells)
        {
            public int Distance(OverlaySignature other)
            {
                var distance = Math.Abs(Lines.Count - other.Lines.Count) * 24;
                var cellCount = Math.Min(Cells.Count, other.Cells.Count);

                for (var i = 0; i < cellCount; i++)
                {
                    if (Cells[i] != other.Cells[i])
                        distance++;
                }

                distance += Math.Abs(Cells.Count - other.Cells.Count);
                return distance;
            }
        }

        private static void WriteEventImages(
            string outputDir,
            Mat frame,
            Mat overlayCrop,
            Mat overlayMask,
            OverlayEvent evt)
        {
            var prefix = $"event_{evt.Frame:D7}_chg_{evt.ChangedPixels:D5}";
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_frame.jpg"), frame);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_overlay.jpg"), overlayCrop);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_mask.png"), overlayMask);
        }

        private static void WriteMissImages(
            VideoCapture capture,
            Mat frame,
            string outputDir,
            OverlayEventCoverage coverage)
        {
            capture.Set(VideoCaptureProperties.PosFrames, coverage.Event.Frame);
            if (!capture.Read(frame) || frame.Empty())
                return;

            var prefix = $"miss_{coverage.Event.Frame:D7}_chg_{coverage.Event.ChangedPixels:D5}";
            using var overlay = new Mat(frame, OverlayBounds);
            using var talkatoo = new Mat(frame, TalkatooBounds);
            using var moonGet = new Mat(frame, MoonGetBounds);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_frame.jpg"), frame);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_overlay.jpg"), overlay);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_talkatoo.jpg"), talkatoo);
            Cv2.ImWrite(Path.Combine(outputDir, $"{prefix}_moonget.jpg"), moonGet);
        }

        private static void WriteCsv(string outputDir, IReadOnlyList<OverlayEvent> events)
        {
            using var writer = new StreamWriter(Path.Combine(outputDir, "overlay-events.csv"));
            writer.WriteLine("frame,active_pixels,changed_pixels,talkatoo_detected,moonget_detected");
            foreach (var evt in events)
            {
                writer.WriteLine(
                    $"{evt.Frame},{evt.ActivePixels},{evt.ChangedPixels},{evt.TalkatooDetected},{evt.MoonGetDetected}");
            }
        }

        private static void WriteCoverageCsv(string outputDir, IReadOnlyList<OverlayEventCoverage> coverages)
        {
            using var writer = new StreamWriter(Path.Combine(outputDir, "overlay-coverage.csv"));
            writer.WriteLine("frame,active_pixels,changed_pixels,talkatoo_frame,moonget_frame,covered");
            foreach (var coverage in coverages)
            {
                writer.WriteLine(
                    $"{coverage.Event.Frame},{coverage.Event.ActivePixels},{coverage.Event.ChangedPixels}," +
                    $"{coverage.TalkatooFrame},{coverage.MoonGetFrame},{coverage.Covered}");
            }
        }

        private readonly record struct OverlayEvent(
            int Frame,
            int ActivePixels,
            int ChangedPixels,
            bool TalkatooDetected,
            bool MoonGetDetected);

        private readonly record struct OverlayEventCoverage(
            OverlayEvent Event,
            int? TalkatooFrame,
            int? MoonGetFrame)
        {
            public bool Covered => TalkatooFrame != null || MoonGetFrame != null;
        }
    }
}
