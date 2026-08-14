using Aviscribe.Core.KingdomDetection;

namespace Aviscribe.Core.Tests;

public sealed class KingdomDetectionTrackerTests
{
    [Fact]
    public void CadenceUsesElapsedTimeInsteadOfFrameCount()
    {
        var tracker = new KingdomDetectionTracker();
        var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.True(tracker.ShouldInspect(start));
        Assert.False(tracker.ShouldInspect(start.AddSeconds(0.9)));
        Assert.True(tracker.ShouldInspect(start.AddSeconds(1)));
    }

    [Fact]
    public void TwoMatchingObservationsConfirmAChange()
    {
        var tracker = new KingdomDetectionTracker();
        var start = DateTime.UtcNow;
        var sand = Match("Sand");

        Assert.Null(tracker.Observe(sand, start, "Cascade"));
        Assert.Equal(
            "Sand",
            tracker.Observe(sand, start.AddSeconds(3), "Cascade"));
    }

    [Fact]
    public void MissingHudBetweenMatchesDoesNotDiscardCandidate()
    {
        var tracker = new KingdomDetectionTracker();
        var start = DateTime.UtcNow;

        Assert.Null(tracker.Observe(Match("Sand"), start, "Cascade"));
        Assert.Null(tracker.Observe(
            KingdomDetectionResult.Rejected(
                KingdomDetectionStatus.HudNotVisible),
            start.AddSeconds(3),
            "Cascade"));
        Assert.Equal(
            "Sand",
            tracker.Observe(Match("Sand"), start.AddSeconds(6), "Cascade"));
    }

    [Fact]
    public void ConflictingAndStaleObservationsRequireFreshConfirmation()
    {
        var tracker = new KingdomDetectionTracker();
        var start = DateTime.UtcNow;

        Assert.Null(tracker.Observe(Match("Sand"), start, "Cascade"));
        Assert.Null(tracker.Observe(Match("Lake"), start.AddSeconds(3), "Cascade"));
        Assert.Null(tracker.Observe(Match("Sand"), start.AddSeconds(6), "Cascade"));
        Assert.Null(tracker.Observe(Match("Sand"), start.AddSeconds(20), "Cascade"));
        Assert.Equal(
            "Sand",
            tracker.Observe(Match("Sand"), start.AddSeconds(23), "Cascade"));
    }

    [Fact]
    public void DetectingCurrentKingdomClearsPendingCandidate()
    {
        var tracker = new KingdomDetectionTracker();
        var start = DateTime.UtcNow;

        Assert.Null(tracker.Observe(Match("Sand"), start, "Cascade"));
        Assert.Null(tracker.Observe(Match("Cascade"), start.AddSeconds(3), "Cascade"));
        Assert.Null(tracker.Observe(Match("Sand"), start.AddSeconds(6), "Cascade"));
    }

    private static KingdomDetectionResult Match(string kingdom)
    {
        return new KingdomDetectionResult(
            KingdomDetectionStatus.Matched,
            kingdom,
            Score: 0.9,
            RunnerUpScore: 0.2);
    }
}
