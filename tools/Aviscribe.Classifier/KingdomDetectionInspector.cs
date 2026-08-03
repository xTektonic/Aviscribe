using Aviscribe.Core.KingdomDetection;
using OpenCvSharp;

namespace Aviscribe.Classifier;

internal static class KingdomDetectionInspector
{
    public static void Print(
        string templateDirectory,
        IEnumerable<string> imagePaths)
    {
        using var detector = new TemplateKingdomDetector(templateDirectory);
        foreach (var imagePath in imagePaths)
        {
            using var image = Cv2.ImRead(imagePath, ImreadModes.Color);
            if (image.Empty())
            {
                Console.WriteLine($"INVALID {imagePath}");
                continue;
            }

            var result = detector.Detect(image);
            Console.WriteLine(
                $"{Path.GetFileName(imagePath)}: {result.Status} " +
                $"{result.Kingdom ?? "-"} score={result.Score:0.0000} " +
                $"runner-up={result.RunnerUpScore:0.0000} " +
                $"margin={result.Score - result.RunnerUpScore:0.0000}");
        }
    }
}
