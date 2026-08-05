using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
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

    [Theory]
    [InlineData(0.0, false)]
    [InlineData(0.25, true)]
    public void KingdomCheckLoggingRequiresNonZeroScore(
        double score,
        bool shouldLog)
    {
        var repository = MoonRepository.LoadDefault();
        var state = new GameState();
        state.SetKingdom("Cascade");
        state.Settings.AutomaticallySwitchKingdoms = true;
        var result = KingdomDetectionResult.Rejected(
            KingdomDetectionStatus.LowConfidence,
            score);
        var kingdomDetector = new ScriptedKingdomDetector(result);
        var diagnostics = new RecordingDiagnostics();
        using var processor = new FrameProcessor(
            new EmptyOcrService(),
            new MoonMatcher(
                repository,
                state.Settings.InputLanguage,
                state.Settings.OutputLanguage),
            state,
            new CountingAbsentTextDetector(),
            diagnostics: diagnostics,
            kingdomDetector: kingdomDetector);

        processor.Start();
        processor.PushFrame(Frame(DateTime.UtcNow));
        Assert.True(SpinWait.SpinUntil(
            () => kingdomDetector.CallCount >= 1,
            TimeSpan.FromSeconds(2)));
        processor.Stop();

        Assert.Equal(
            shouldLog,
            diagnostics.Messages.Any(message =>
                message.StartsWith("KINGDOM DETECTION", StringComparison.Ordinal)));
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

    private sealed class RecordingDiagnostics : IAppDiagnostics
    {
        private readonly object _sync = new();
        private readonly List<string> _messages = [];

        public string LogDirectory => string.Empty;

        public IReadOnlyList<DiagnosticEntry> RecentEntries => [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_sync)
                    return _messages.ToArray();
            }
        }

        public void Debug(string message)
        {
            lock (_sync)
                _messages.Add(message);
        }

        public void Information(string message)
        {
        }

        public void Error(string message, Exception? exception = null)
        {
        }

        public void Dispose()
        {
        }
    }
}
