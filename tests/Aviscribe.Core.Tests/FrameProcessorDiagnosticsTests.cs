using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Core.Tests;

public sealed class FrameProcessorDiagnosticsTests
{
    private static readonly Rect[] StoryBackgroundPatches =
    [
        new(1480, 876, 24, 24),
        new(1524, 824, 24, 24),
        new(1396, 828, 24, 24),
        new(700, 944, 24, 24)
    ];

    private static readonly Scalar[] StoryBackgroundColors =
    [
        new(2.1, 0.7, 225.1),
        new(2.7, 0.9, 224.9),
        new(9.4, 4.1, 224.6),
        new(10.7, 5.0, 225.3)
    ];

    [Fact]
    public async Task OcrEventsAreAlwaysLogged()
    {
        var diagnostics = new RecordingDiagnostics();
        using var ocr = new RecordingOcrService("diagnostic probe");
        var state = new GameState();
        state.Settings.AdaptiveTalkatooDetection = true;
        state.SetKingdom("Cascade");
        var matcher = new MoonMatcher(
            new MoonRepository(),
            state.Settings.InputLanguage,
            state.Settings.OutputLanguage);
        using var detector = new TalkatooOnlyDetector();
        using var processor = new FrameProcessor(
            ocr,
            matcher,
            state,
            detector,
            diagnostics: diagnostics);

        processor.Start();
        try
        {
            var start = new DateTime(
                2026,
                1,
                1,
                0,
                0,
                0,
                DateTimeKind.Utc);
            for (var index = 0;
                index < TalkatooConfirmationTracker.RequiredStableFrames;
                index++)
            {
                processor.PushFrame(new VideoFrame(
                    new Mat(
                        new Size(1920, 1080),
                        MatType.CV_8UC3,
                        Scalar.Black),
                    start +
                        CaptureTiming.PreferredFrameInterval * index));

                Assert.True(
                    await detector.Detected.WaitAsync(
                        TimeSpan.FromSeconds(10),
                        TestContext.Current.CancellationToken),
                    $"Frame {index + 1} was not inspected.");
            }

            Assert.True(
                ocr.Read.Wait(
                    TimeSpan.FromSeconds(10),
                    TestContext.Current.CancellationToken),
                "The diagnostic test did not reach OCR.");
            await Task.Delay(
                50,
                TestContext.Current.CancellationToken);
        }
        finally
        {
            processor.Stop();
        }

        Assert.Contains(
            diagnostics.Messages,
            message => message.StartsWith(
                "ENQUEUE OCR (Talkatoo",
                StringComparison.Ordinal));
        Assert.Contains(
            "OCR RESULT (Talkatoo): \"diagnostic probe\"",
            diagnostics.Messages);
    }

    [Fact]
    public async Task AdaptiveTalkatooDoesNotBlockStoryMoonOcr()
    {
        var repository = MoonRepository.LoadDefault();
        var storyMoon = repository.Moons.First(moon =>
            moon.IsStory && !string.IsNullOrWhiteSpace(moon.ChineseTraditional));
        var diagnostics = new RecordingDiagnostics();
        using var ocr = new RecordingOcrService(storyMoon.ChineseTraditional);
        var state = new GameState();
        state.Settings.AdaptiveTalkatooDetection = true;
        state.SetKingdom(storyMoon.Kingdom);
        var matcher = new MoonMatcher(
            repository,
            state.Settings.InputLanguage,
            state.Settings.OutputLanguage);
        using var processor = new FrameProcessor(
            ocr,
            matcher,
            state,
            diagnostics: diagnostics);
        using var frame = CreateStoryMoonFrame();

        processor.Start();
        try
        {
            var start = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            for (var index = 0; index < 12 && !ocr.Read.IsSet; index++)
            {
                processor.PushFrame(new VideoFrame(
                    frame.Clone(),
                    start + CaptureTiming.PreferredFrameInterval * index));
                await Task.Delay(20, TestContext.Current.CancellationToken);
            }

            Assert.True(
                ocr.Read.Wait(
                    TimeSpan.FromSeconds(5),
                    TestContext.Current.CancellationToken),
                "Story-moon OCR was not enqueued while adaptive Talkatoo detection was enabled.");
        }
        finally
        {
            processor.Stop();
        }

        Assert.Contains(
            diagnostics.Messages,
            message => message.StartsWith(
                "ENQUEUE OCR (StoryMoon",
                StringComparison.Ordinal));
        Assert.DoesNotContain(
            diagnostics.Messages,
            message => message.StartsWith(
                "ENQUEUE OCR (Talkatoo",
                StringComparison.Ordinal));
    }

    private static Mat CreateStoryMoonFrame()
    {
        var image = new Mat(
            new Size(1920, 1080),
            MatType.CV_8UC3,
            Scalar.Black);
        for (var index = 0; index < StoryBackgroundPatches.Length; index++)
        {
            Cv2.Rectangle(
                image,
                StoryBackgroundPatches[index],
                StoryBackgroundColors[index],
                thickness: -1);
        }
        return image;
    }

    private sealed class RecordingOcrService(string result) :
        IOcrService,
        IDisposable
    {
        public ManualResetEventSlim Read { get; } = new(false);

        public string ReadText(Mat frame)
        {
            Read.Set();
            return result;
        }

        public void Dispose()
        {
            Read.Dispose();
        }
    }

    private sealed class TalkatooOnlyDetector :
        ITextPresenceDetector,
        IDisposable
    {
        public SemaphoreSlim Detected { get; } = new(0);

        public TextPresenceResult Detect(
            OcrRegionType regionType,
            Mat image)
        {
            if (regionType != OcrRegionType.Talkatoo)
                return TextPresenceResult.Absent(nameof(TalkatooOnlyDetector));

            Detected.Release();
            return TextPresenceResult.PresentResult(
                nameof(TalkatooOnlyDetector));
        }

        public void Dispose()
        {
            Detected.Dispose();
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
