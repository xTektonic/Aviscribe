using Aviscribe.Core.KingdomDetection;
using OpenCvSharp;

namespace Aviscribe.Classifier;

internal static class KingdomDetectionVideoRegressionSuite
{
    private static readonly PositiveExpectation[] PositiveExpectations =
    [
        new("Cascade", [45_000, 45_180]),
        new("Sand", [77_760, 77_940, 78_120]),
        new("Wooded", [289_500, 289_680, 289_860, 290_040, 290_220]),
        new("Lake", [463_740, 463_920, 464_100]),
        new("Lost", [495_720, 495_840, 495_960, 496_080]),
        new("Metro", [575_400, 575_580, 575_760]),
        new("Seaside", [702_960, 703_140, 703_320, 703_500, 703_680]),
        new("Snow", [949_260, 949_620, 949_800]),
        new("Luncheon", [1_024_380, 1_024_560, 1_024_740]),
        new("Bowsers", [1_149_600, 1_149_780, 1_149_960, 1_150_140, 1_150_320]),
        new("Moon", [1_268_700, 1_268_880, 1_269_060, 1_269_240, 1_269_420]),
        new("Mushroom", [1_284_600, 1_284_780, 1_284_960]),
        new("Cap", [1_385_580, 1_385_700, 1_386_360])
    ];

    private static readonly NegativeWindow[] NegativeWindows =
    [
        new("Cloud", 489_600, 492_600),
        new("Ruined", 1_126_800, 1_137_600),
        new("Dark", 1_300_800, 1_353_000)
    ];

    private static readonly int[] TransitionFrames =
    [
        495_540,   // Lost: black transition
        1_385_460, // Cap: HUD still being exposed
        1_385_880, // Cap: HUD hidden during moon collection
        1_386_060  // Cap: collection overlay
    ];

    public static void Run(string videoPath, string templateDirectory)
    {
        using var capture = new VideoCapture(videoPath);
        if (!capture.IsOpened())
            throw new InvalidOperationException($"Could not open video: {videoPath}");

        using var detector = new TemplateKingdomDetector(templateDirectory);
        using var frame = new Mat();
        var failures = new List<string>();
        var positiveCount = 0;
        var negativeCount = 0;

        foreach (var expectation in PositiveExpectations)
        {
            foreach (var frameIndex in expectation.Frames)
            {
                var result = ReadAndDetect(
                    capture,
                    detector,
                    frame,
                    frameIndex);
                positiveCount++;
                if (!result.IsMatch ||
                    !expectation.Kingdom.Equals(
                        result.Kingdom,
                        StringComparison.OrdinalIgnoreCase))
                {
                    failures.Add(
                        $"frame {frameIndex}: expected {expectation.Kingdom}, " +
                        $"got {Describe(result)}");
                }
            }
        }

        var stride = (int)Math.Round(
            capture.Get(VideoCaptureProperties.Fps) *
            KingdomDetectionTracker.DefaultInspectionInterval.TotalSeconds);
        stride = Math.Max(1, stride);
        foreach (var window in NegativeWindows)
        {
            for (var frameIndex = window.StartFrame;
                 frameIndex <= window.EndFrame;
                 frameIndex += stride)
            {
                var result = ReadAndDetect(
                    capture,
                    detector,
                    frame,
                    frameIndex);
                negativeCount++;
                if (result.IsMatch)
                {
                    failures.Add(
                        $"frame {frameIndex} in {window.Name}: expected no match, " +
                        $"got {Describe(result)}");
                }
            }
        }

        foreach (var frameIndex in TransitionFrames)
        {
            var result = ReadAndDetect(
                capture,
                detector,
                frame,
                frameIndex);
            negativeCount++;
            if (result.IsMatch)
            {
                failures.Add(
                    $"frame {frameIndex} in a HUD transition: expected no match, " +
                    $"got {Describe(result)}");
            }
        }

        if (failures.Count > 0)
        {
            foreach (var failure in failures.Take(30))
                Console.WriteLine($"FAIL {failure}");
            if (failures.Count > 30)
                Console.WriteLine($"... and {failures.Count - 30} more failures");

            throw new InvalidOperationException(
                $"Kingdom detection regression failed: {failures.Count} issue(s).");
        }

        Console.WriteLine(
            $"PASS kingdom detection regression: {positiveCount} labeled HUD " +
            $"frames and {negativeCount} unsupported-kingdom samples.");
    }

    private static KingdomDetectionResult ReadAndDetect(
        VideoCapture capture,
        TemplateKingdomDetector detector,
        Mat frame,
        int frameIndex)
    {
        capture.Set(VideoCaptureProperties.PosFrames, frameIndex);
        if (!capture.Read(frame) || frame.Empty())
        {
            throw new InvalidOperationException(
                $"Could not read video frame {frameIndex}.");
        }

        return detector.Detect(frame);
    }

    private static string Describe(KingdomDetectionResult result)
    {
        return result.IsMatch
            ? $"{result.Kingdom} ({result.Score:0.000})"
            : $"{result.Status} ({result.Score:0.000})";
    }

    private sealed record PositiveExpectation(
        string Kingdom,
        IReadOnlyList<int> Frames);

    private sealed record NegativeWindow(
        string Name,
        int StartFrame,
        int EndFrame);
}
