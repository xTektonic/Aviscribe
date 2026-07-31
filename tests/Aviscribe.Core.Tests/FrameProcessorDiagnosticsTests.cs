using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Core.Tests;

public sealed class FrameProcessorDiagnosticsTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task OcrEventsFollowDebugLoggingSetting(bool debugEnabled)
    {
        var diagnostics = new RecordingDiagnostics
        {
            DebugEnabled = debugEnabled
        };
        using var ocr = new RecordingOcrService("diagnostic probe");
        var state = new GameState();
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
            for (var index = 0;
                index < TalkatooConfirmationTracker.RequiredStableFrames;
                index++)
            {
                processor.PushFrame(new VideoFrame(
                    new Mat(
                        new Size(1920, 1080),
                        MatType.CV_8UC3,
                        Scalar.Black),
                    DateTime.UtcNow));

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

        if (debugEnabled)
        {
            Assert.Contains(
                diagnostics.Messages,
                message => message.StartsWith(
                    "ENQUEUE OCR (Talkatoo",
                    StringComparison.Ordinal));
            Assert.Contains(
                "OCR RESULT (Talkatoo): \"diagnostic probe\"",
                diagnostics.Messages);
        }
        else
        {
            Assert.Empty(diagnostics.Messages);
        }
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

        public bool DebugEnabled { get; set; }
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
            if (!DebugEnabled)
                return;

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
