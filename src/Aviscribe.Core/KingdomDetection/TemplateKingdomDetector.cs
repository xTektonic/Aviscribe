using OpenCvSharp;

namespace Aviscribe.Core.KingdomDetection;

public sealed class TemplateKingdomDetector : IKingdomDetector, IDisposable
{
    public static readonly Rect IconTemplateBounds = new(245, 35, 82, 90);
    public static readonly Rect IconSearchBounds = new(238, 28, 96, 104);
    public static readonly Rect HudUnderlineBounds = new(62, 124, 356, 22);

    public const double DefaultMinimumScore = 0.40;
    public const double DefaultMinimumMargin = 0.06;

    private static readonly IReadOnlyDictionary<string, string> TemplateFiles =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Cascade"] = "cascade.png",
            ["Sand"] = "sand.png",
            ["Wooded"] = "wooded.png",
            ["Lake"] = "lake.png",
            ["Lost"] = "lost.png",
            ["Metro"] = "metro.png",
            ["Seaside"] = "seaside.png",
            ["Snow"] = "snow.png",
            ["Luncheon"] = "luncheon.png",
            ["Bowsers"] = "bowsers.png",
            ["Moon"] = "moon.png",
            ["Mushroom"] = "mushroom.png",
            ["Cap"] = "cap.png"
        };

    private readonly List<KingdomTemplate> _templates = [];
    private readonly double _minimumScore;
    private readonly double _minimumMargin;
    private readonly bool _requireHud;
    private bool _disposed;

    public TemplateKingdomDetector(
        string templateDirectory,
        double minimumScore = DefaultMinimumScore,
        double minimumMargin = DefaultMinimumMargin,
        bool requireHud = true)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateDirectory);
        if (minimumScore is < -1 or > 1)
            throw new ArgumentOutOfRangeException(nameof(minimumScore));
        if (minimumMargin is < 0 or > 2)
            throw new ArgumentOutOfRangeException(nameof(minimumMargin));

        _minimumScore = minimumScore;
        _minimumMargin = minimumMargin;
        _requireHud = requireHud;

        try
        {
            foreach (var item in TemplateFiles)
            {
                var path = Path.Combine(templateDirectory, item.Value);
                using var source = Cv2.ImRead(path, ImreadModes.Color);
                if (source.Empty())
                    throw new FileNotFoundException(
                        $"Could not load the {item.Key} kingdom icon template.",
                        path);

                using var resized = ResizeTemplate(source);
                _templates.Add(new KingdomTemplate(
                    item.Key,
                    CreateEdgeImage(resized)));
            }
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public KingdomDetectionResult Detect(Mat referenceFrame)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(referenceFrame);
        if (referenceFrame.Empty() ||
            referenceFrame.Width < IconSearchBounds.Right ||
            referenceFrame.Height < HudUnderlineBounds.Bottom)
        {
            return KingdomDetectionResult.Rejected(
                KingdomDetectionStatus.InvalidFrame);
        }

        if (_requireHud && !IsHudVisible(referenceFrame))
        {
            return KingdomDetectionResult.Rejected(
                KingdomDetectionStatus.HudNotVisible);
        }

        using var search = new Mat(referenceFrame, IconSearchBounds);
        using var searchEdges = CreateEdgeImage(search);

        var scores = new List<(string Kingdom, double Score)>(_templates.Count);
        foreach (var template in _templates)
        {
            using var correlation = new Mat();
            Cv2.MatchTemplate(
                searchEdges,
                template.Edges,
                correlation,
                TemplateMatchModes.CCoeffNormed);
            Cv2.MinMaxLoc(
                correlation,
                out _,
                out var maximum,
                out _,
                out _);
            scores.Add((
                template.Kingdom,
                double.IsFinite(maximum) ? maximum : -1));
        }

        var ordered = scores
            .OrderByDescending(item => item.Score)
            .ToArray();
        var best = ordered[0];
        var runnerUp = ordered[1];
        if (best.Score < _minimumScore)
        {
            return KingdomDetectionResult.Rejected(
                KingdomDetectionStatus.LowConfidence,
                best.Score,
                runnerUp.Score);
        }

        if (best.Score - runnerUp.Score < _minimumMargin)
        {
            return KingdomDetectionResult.Rejected(
                KingdomDetectionStatus.Ambiguous,
                best.Score,
                runnerUp.Score);
        }

        return new KingdomDetectionResult(
            KingdomDetectionStatus.Matched,
            best.Kingdom,
            best.Score,
            runnerUp.Score);
    }

    internal static bool IsHudVisible(Mat referenceFrame)
    {
        using var underline = new Mat(referenceFrame, HudUnderlineBounds);
        using var grayscale = CreateGrayscale(underline);
        using var bright = new Mat();
        Cv2.Threshold(
            grayscale,
            bright,
            165,
            255,
            ThresholdTypes.Binary);

        using var rowSums = new Mat();
        Cv2.Reduce(
            bright,
            rowSums,
            ReduceDimension.Column,
            ReduceTypes.Sum,
            (int)MatType.CV_32SC1);
        Cv2.MinMaxLoc(
            rowSums,
            out _,
            out var maximum,
            out _,
            out _);
        return maximum >= 220 * 255;
    }

    private static Mat ResizeTemplate(Mat source)
    {
        if (source.Width == IconTemplateBounds.Width &&
            source.Height == IconTemplateBounds.Height)
        {
            return source.Clone();
        }

        var resized = new Mat();
        Cv2.Resize(
            source,
            resized,
            IconTemplateBounds.Size,
            interpolation: InterpolationFlags.Area);
        return resized;
    }

    private static Mat CreateEdgeImage(Mat source)
    {
        using var grayscale = CreateGrayscale(source);
        using var blurred = new Mat();
        Cv2.GaussianBlur(
            grayscale,
            blurred,
            new Size(3, 3),
            sigmaX: 0);

        var edges = new Mat();
        Cv2.Canny(blurred, edges, 55, 150);
        return edges;
    }

    private static Mat CreateGrayscale(Mat source)
    {
        if (source.Channels() == 1)
            return source.Clone();

        var grayscale = new Mat();
        Cv2.CvtColor(
            source,
            grayscale,
            source.Channels() == 4
                ? ColorConversionCodes.BGRA2GRAY
                : ColorConversionCodes.BGR2GRAY);
        return grayscale;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        foreach (var template in _templates)
            template.Edges.Dispose();
        _templates.Clear();
    }

    private sealed record KingdomTemplate(string Kingdom, Mat Edges);
}
