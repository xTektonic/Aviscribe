using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class MoonGetInspector
    {
        public static void Print(IEnumerable<string> paths)
        {
            foreach (var path in paths)
            {
                using var image = Cv2.ImRead(path);
                if (image.Empty())
                {
                    Console.WriteLine($"{path}: could not read image");
                    continue;
                }

                var metrics = MoonGetExperiment.Measure(image);
                Console.WriteLine(Path.GetFileName(path));
                Console.WriteLine($"  detected: {TextDetection.HasMoonText(image)}");
                Console.WriteLine(
                    $"  pix {metrics.WhitePixels}, ratio {metrics.WhiteRatio:0.000}, band {metrics.BandScore:0}, " +
                    $"rows {metrics.BandRows}, center {metrics.BandCenterRatio:0.00}, cols {metrics.ActiveColumns}, span {metrics.SpanWidth}");
                Console.WriteLine(
                    $"  comps {metrics.TextComponentCount}, compArea {metrics.TextComponentArea}, compSpan {metrics.TextComponentSpan}, " +
                    $"outline {metrics.OutlinedPixels}, outlineCols {metrics.OutlinedColumns}");
            }
        }
    }
}
