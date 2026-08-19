namespace Aviscribe.Core.Capture;

/// <summary>
/// Shared capture-rate preferences and conversions used by capture and
/// time-based detection policies.
/// </summary>
public static class CaptureTiming
{
    public const int PreferredFramesPerSecond = 60;

    public static TimeSpan PreferredFrameInterval { get; } =
        TimeSpan.FromSeconds(1.0 / PreferredFramesPerSecond);

    public static TimeSpan DurationForFrames(int frameCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(frameCount);
        return TimeSpan.FromSeconds(
            frameCount / (double)PreferredFramesPerSecond);
    }

    public static TimeSpan DurationBetweenObservations(int observationCount)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(observationCount);
        return DurationForFrames(Math.Max(0, observationCount - 1));
    }

    public static bool HasElapsed(TimeSpan elapsed, TimeSpan required)
    {
        // DateTime and device timestamps round fractional frame periods to
        // different tick boundaries. A sub-millisecond tolerance prevents a
        // nominal 60 fps boundary from slipping by a whole frame.
        var tolerance = TimeSpan.FromMilliseconds(1);
        return elapsed >= required ||
            required > TimeSpan.Zero && elapsed >= required - tolerance;
    }
}
