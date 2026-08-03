namespace Aviscribe.Core.KingdomDetection;

public enum KingdomDetectionStatus
{
    Matched,
    InvalidFrame,
    HudNotVisible,
    LowConfidence,
    Ambiguous
}

public sealed record KingdomDetectionResult(
    KingdomDetectionStatus Status,
    string? Kingdom,
    double Score,
    double RunnerUpScore)
{
    public bool IsMatch =>
        Status == KingdomDetectionStatus.Matched &&
        !string.IsNullOrWhiteSpace(Kingdom);

    public static KingdomDetectionResult Rejected(
        KingdomDetectionStatus status,
        double score = 0,
        double runnerUpScore = 0)
    {
        if (status == KingdomDetectionStatus.Matched)
            throw new ArgumentOutOfRangeException(nameof(status));

        return new KingdomDetectionResult(
            status,
            Kingdom: null,
            score,
            runnerUpScore);
    }
}
