using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class StoryMoonExperiment
    {
        private static readonly Rect ReferenceNameCrop = new(450, 820, 1100, 150);
        private const int ReferenceWidth = 1920;
        private const int ReferenceHeight = 1080;
        private const int LabeledEventNeighborhood = 4;

        public static void PrintSummary(string dataRoot)
        {
            var positives = Load(Path.Combine(dataRoot, "StoryMoons")).ToArray();
            var mixed = Load(Path.Combine(dataRoot, "StoryMoonData")).ToArray();
            var labeledFrames = positives
                .Where(sample => sample.FrameNumber != null)
                .Select(sample => sample.FrameNumber!.Value)
                .ToArray();

            var misses = positives
                .Where(sample => !sample.Detected)
                .ToArray();
            var mixedHits = mixed
                .Where(sample => sample.Detected)
                .ToArray();
            var reviewHits = mixedHits
                .Where(sample => DistanceFromNearestLabel(sample, labeledFrames) > LabeledEventNeighborhood)
                .ToArray();

            Console.WriteLine(
                $"StoryMoon production detector: {positives.Length - misses.Length}/{positives.Length} " +
                $"labeled positives detected; {mixedHits.Length}/{mixed.Length} mixed samples detected; " +
                $"{reviewHits.Length} hit(s) outside a +/-{LabeledEventNeighborhood}-frame labeled event neighborhood.");

            if (misses.Length > 0)
            {
                Console.WriteLine("Missed labeled positives:");
                foreach (var sample in misses)
                    Console.WriteLine($"  FN {Path.GetFileName(sample.Path)}");
            }

            if (reviewHits.Length > 0)
            {
                Console.WriteLine("Mixed-data hits requiring review:");
                foreach (var sample in reviewHits)
                {
                    var distance = DistanceFromNearestLabel(sample, labeledFrames);
                    Console.WriteLine(
                        $"  REVIEW {Path.GetFileName(sample.Path)} " +
                        $"(nearest labeled frame distance: {FormatDistance(distance)})");
                }
            }
        }

        public static Rect ScaleNameCrop(int width, int height)
        {
            var xScale = width / (double)ReferenceWidth;
            var yScale = height / (double)ReferenceHeight;

            var x = (int)Math.Round(ReferenceNameCrop.X * xScale);
            var y = (int)Math.Round(ReferenceNameCrop.Y * yScale);
            var w = (int)Math.Round(ReferenceNameCrop.Width * xScale);
            var h = (int)Math.Round(ReferenceNameCrop.Height * yScale);

            x = Math.Clamp(x, 0, Math.Max(0, width - 1));
            y = Math.Clamp(y, 0, Math.Max(0, height - 1));
            w = Math.Clamp(w, 1, width - x);
            h = Math.Clamp(h, 1, height - y);

            return new Rect(x, y, w, h);
        }

        private static IEnumerable<Sample> Load(string directory)
        {
            if (!Directory.Exists(directory))
                yield break;

            foreach (var path in Directory
                .EnumerateFiles(directory)
                .Where(DatasetInspector.IsImage))
            {
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                    continue;

                using var crop = new Mat(image, ScaleNameCrop(image.Width, image.Height));
                yield return new Sample(
                    path,
                    ParseFrameNumber(path),
                    TextDetection.HasStoryMoonText(crop));
            }
        }

        private static int? ParseFrameNumber(string path)
        {
            var fileName = Path.GetFileNameWithoutExtension(path);
            var separator = fileName.LastIndexOf('_');
            if (separator < 0 || separator == fileName.Length - 1)
                return null;

            return int.TryParse(fileName[(separator + 1)..], out var frameNumber)
                ? frameNumber
                : null;
        }

        private static int DistanceFromNearestLabel(
            Sample sample,
            IReadOnlyList<int> labeledFrames)
        {
            if (sample.FrameNumber is not { } frameNumber || labeledFrames.Count == 0)
                return int.MaxValue;

            return labeledFrames.Min(labeledFrame => Math.Abs(frameNumber - labeledFrame));
        }

        private static string FormatDistance(int distance)
        {
            return distance == int.MaxValue ? "unknown" : distance.ToString();
        }

        private sealed record Sample(
            string Path,
            int? FrameNumber,
            bool Detected);
    }
}
