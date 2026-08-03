using Aviscribe.Core.Capture;
using Aviscribe.Core.KingdomDetection;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Core.Tests;

public sealed class FrameProcessorKingdomDetectionTests
{
    [Fact]
    public void TwoTimedDetectionsSwitchTheActiveKingdom()
    {
        var repository = MoonRepository.LoadDefault();
        var state = new GameState();
        state.SetKingdom("Cascade");
        state.Settings.AutomaticallySwitchKingdoms = true;
        var kingdomDetector = new ScriptedKingdomDetector(Match("Sand"));
        using var processor = new FrameProcessor(
            new EmptyOcrService(),
            new MoonMatcher(
                repository,
                state.Settings.InputLanguage,
                state.Settings.OutputLanguage),
            state,
            new CountingAbsentTextDetector(),
            kingdomDetector: kingdomDetector);
        var start = DateTime.UtcNow;

        processor.Start();
        processor.PushFrame(Frame(start));
        Assert.True(SpinWait.SpinUntil(
            () => kingdomDetector.CallCount >= 1,
            TimeSpan.FromSeconds(2)));
        Assert.Equal("Cascade", state.CurrentKingdom);

        processor.PushFrame(Frame(start.AddSeconds(3)));

        Assert.True(SpinWait.SpinUntil(
            () => state.CurrentKingdom == "Sand",
            TimeSpan.FromSeconds(2)));
    }

    [Fact]
    public void DisabledSettingSkipsTheKingdomDetector()
    {
        var repository = MoonRepository.LoadDefault();
        var state = new GameState();
        state.SetKingdom("Cascade");
        var textDetector = new CountingAbsentTextDetector();
        var kingdomDetector = new ScriptedKingdomDetector(Match("Sand"));
        using var processor = new FrameProcessor(
            new EmptyOcrService(),
            new MoonMatcher(
                repository,
                state.Settings.InputLanguage,
                state.Settings.OutputLanguage),
            state,
            textDetector,
            kingdomDetector: kingdomDetector);

        processor.Start();
        processor.PushFrame(Frame(DateTime.UtcNow));

        Assert.True(SpinWait.SpinUntil(
            () => textDetector.CallCount > 0,
            TimeSpan.FromSeconds(2)));
        Assert.Equal(0, kingdomDetector.CallCount);
        Assert.Equal("Cascade", state.CurrentKingdom);
    }

    [Fact]
    public void HiddenPostgameKingdomIsNotSelectedAutomatically()
    {
        var repository = MoonRepository.LoadDefault();
        var state = new GameState();
        state.SetKingdom("Cascade");
        state.Settings.AutomaticallySwitchKingdoms = true;
        var kingdomDetector = new ScriptedKingdomDetector(Match("Mushroom"));
        using var processor = new FrameProcessor(
            new EmptyOcrService(),
            new MoonMatcher(
                repository,
                state.Settings.InputLanguage,
                state.Settings.OutputLanguage),
            state,
            new CountingAbsentTextDetector(),
            kingdomDetector: kingdomDetector);
        var start = DateTime.UtcNow;

        processor.Start();
        processor.PushFrame(Frame(start));
        Assert.True(SpinWait.SpinUntil(
            () => kingdomDetector.CallCount >= 1,
            TimeSpan.FromSeconds(2)));
        processor.PushFrame(Frame(start.AddSeconds(3)));
        Assert.True(SpinWait.SpinUntil(
            () => kingdomDetector.CallCount >= 2,
            TimeSpan.FromSeconds(2)));

        Assert.Equal("Cascade", state.CurrentKingdom);
    }

    private static VideoFrame Frame(DateTime timestamp)
    {
        return new VideoFrame(
            new Mat(1080, 1920, MatType.CV_8UC3, Scalar.Black),
            timestamp);
    }

    private static KingdomDetectionResult Match(string kingdom)
    {
        return new KingdomDetectionResult(
            KingdomDetectionStatus.Matched,
            kingdom,
            Score: 0.9,
            RunnerUpScore: 0.2);
    }

    private sealed class EmptyOcrService : IOcrService
    {
        public string ReadText(Mat frame) => string.Empty;
    }

    private sealed class CountingAbsentTextDetector : ITextPresenceDetector
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
        {
            Interlocked.Increment(ref _callCount);
            return TextPresenceResult.Absent(nameof(CountingAbsentTextDetector));
        }
    }

    private sealed class ScriptedKingdomDetector(
        KingdomDetectionResult result) : IKingdomDetector
    {
        private int _callCount;
        public int CallCount => Volatile.Read(ref _callCount);

        public KingdomDetectionResult Detect(Mat referenceFrame)
        {
            Interlocked.Increment(ref _callCount);
            return result;
        }
    }
}
