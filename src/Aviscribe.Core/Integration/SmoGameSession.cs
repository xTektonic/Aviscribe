using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using Aviscribe.Core.Ocr;
using Aviscribe.Core.Online;
using SMOVision.Core;
using SMOVision.Core.Capture;
using SMOVision.Vision;
using SMOVision.Vision.Ocr.Onnx;

namespace Aviscribe.Core.Integration;

/// <summary>Owns recognition lifetime and projects SMOVision evidence into Aviscribe run rules.</summary>
public sealed class SmoGameSession : IAsyncDisposable
{
    private readonly GameState _state;
    private readonly MoonRepository _repository;
    private readonly RunCoordinator _coordinator;
    private readonly IAppDiagnostics _diagnostics;
    private readonly object _sync = new();
    private CaptureCropSettings _crop;
    private string _policy;
    private long _sequence, _minimumSequence;
    private int _reset;
    private bool _accepting, _disposed;
    private PushFrameSource? _input;
    private CancellationTokenSource? _cancel;
    private Task? _run;
    private OnnxTextReader? _lastReader;
    private readonly HashSet<(Guid Session, Guid Occurrence)> _handled = [];
    private readonly Queue<(Guid Session, Guid Occurrence)> _handledOrder = new();
    private readonly HashSet<string> _reviews = new(StringComparer.Ordinal);
    private readonly Queue<string> _reviewOrder = new();
    internal Func<RunSettings, IReadOnlyList<IDetector>>? DetectorFactory { get; init; }
    public event EventHandler<AmbiguousOcrResult>? AmbiguousMatchReceived;
    public event Action<string>? ProcessingFailed;
    public AnalysisResult? Result { get; private set; }

    public Aviscribe.Core.Ocr.OcrRuntimeStatus? RuntimeStatus
    {
        get
        {
            var status = _lastReader?.Runtime;
            return status is null ? null : new(_state.Settings.OcrMode, status.ActiveProvider, status.Device, status.FallbackReason);
        }
    }

    public SmoGameSession(GameState state, MoonRepository repository, CaptureCropSettings crop,
        IAppDiagnostics diagnostics, RunCoordinator coordinator)
    {
        _state = state; _repository = repository; _crop = crop.Clone();
        _diagnostics = diagnostics; _coordinator = coordinator; _policy = Policy();
        _state.Changed += StateChanged;
        _coordinator.CaptureContextInvalidated += ContextInvalidated;
    }

    private void ContextInvalidated(object? sender, EventArgs e) => Invalidate();

    private string Policy() => $"{_state.CurrentKingdom}|{_state.Settings.Category}|{_state.Settings.IncludePostGameKingdoms}|{_state.Settings.AutomaticallySwitchKingdoms}|{_state.Settings.AdaptiveTalkatooDetection}|{_state.Settings.InputLanguage}|{_state.Settings.OcrMode}";
    private void StateChanged(object? sender, EventArgs e)
    {
        lock (_sync)
        {
            var policy = Policy();
            if (policy == _policy) return;
            _policy = policy; Invalidate();
        }
    }

    public void Invalidate()
    {
        lock (_sync)
        {
            _minimumSequence = _sequence + 1;
            _reviews.Clear(); _reviewOrder.Clear();
            Interlocked.Exchange(ref _reset, 1);
        }
    }

    public void UpdateCrop(CaptureCropSettings crop)
    { lock (_sync) { _crop = crop.Clone(); Invalidate(); } }

    public void Start()
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_run is not null) return;
            _accepting = true; _cancel = new();
            _input = new PushFrameSource("gameplay", ProcessingMode.Live);
            var input = _input; var token = _cancel.Token;
            _run = Task.Run(() => RunAsync(input, token), CancellationToken.None);
        }
    }

    /// <summary>Takes ownership even when stopped. The SMOVision live queue retains at most one pending frame.</summary>
    public void PushFrame(VideoFrame frame)
    {
        using (frame)
        {
            lock (_sync)
            {
                if (!_accepting || _input is null) return;
                using var original = frame.RetainBuffer();
                var lease = original.Retain(original.Stamp with { Sequence = ++_sequence });
                // Live PushFrameSource never waits for capacity. Observe closure during shutdown.
                var submitted = _input.SubmitOwnedAsync(lease);
                if (!submitted.IsCompletedSuccessfully) _ = ObserveSubmissionAsync(submitted);
            }
        }
    }
    private async Task ObserveSubmissionAsync(ValueTask submitted)
    {
        try { await submitted.ConfigureAwait(false); }
        catch (Exception error) { _diagnostics.Debug($"SMOVision frame input closed: {error.Message}"); }
    }

    public async Task StopAsync()
    {
        Task? run;
        lock (_sync)
        {
            _accepting = false; Invalidate(); _cancel?.Cancel(); _input?.Complete(); run = _run;
        }
        try { if (run is not null) await run.ConfigureAwait(false); }
        finally { lock (_sync) { _run = null; _input = null; _cancel?.Dispose(); _cancel = null; } }
    }

    private IReadOnlyList<IDetector> CreateDetectors(RunSettings settings)
    {
        if (DetectorFactory is not null) return DetectorFactory(settings);
        ITextReader Reader()
        {
            var reader = new OnnxTextReader(OnnxReaderOptions.ForLanguage(SmoCatalog.Language(settings.InputLanguage), forceCpu: settings.OcrMode == OcrMode.Cpu));
            _lastReader = reader;
            return reader;
        }
        var detectors = new List<IDetector>
        {
            new MoonOverlayDetector(MoonOverlayKind.Collection, Reader, SmoCatalog.Language(settings.InputLanguage)),
            new MoonOverlayDetector(MoonOverlayKind.Talkatoo, Reader, SmoCatalog.Language(settings.InputLanguage), settings.AdaptiveTalkatooDetection)
        };
        if (settings.AutomaticallySwitchKingdoms) detectors.Add(new KingdomDetector());
        return detectors;
    }

    /// <summary>Offline developer replays use the same detectors, sink, eligibility and run projection as live capture.</summary>
    public async Task RunAsync(IFrameSource source, CancellationToken cancellationToken = default)
    {
        var settings = _state.Settings.Clone();
        var queue = Channel.CreateBounded<Notice>(new BoundedChannelOptions(128) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
        using var processing = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var consumer = ConsumeAsync(queue.Reader, processing.Token);
        var prepared = new PreparedSource(this, source);
        VisionSession? session = null;
        try
        {
            session = VisionSession.CreateBuilder().WithSource(prepared)
                .Enable(CreateDetectors(settings).ToArray()).WithSink(new Sink(queue.Writer)).Build();
            prepared.Session = session;
            await session.RunAsync(processing.Token).ConfigureAwait(false);
            Result = session.Result;
            if (Result.Status == AnalysisStatus.Failed) ReportFailure($"SMOVision processing failed: {Result.Failure}");
        }
        catch (OperationCanceledException) when (processing.IsCancellationRequested) { }
        catch (Exception error) { _diagnostics.Error("SMOVision processing failed.", error); throw; }
        finally
        {
            Result = session?.Result;
            queue.Writer.TryComplete();
            await consumer.ConfigureAwait(false);
            if (session is not null) await session.DisposeAsync().ConfigureAwait(false);
            else await prepared.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task ConsumeAsync(ChannelReader<Notice> reader, CancellationToken token)
    {
        // Always drain the sink, including after logical cancellation, so disposal cannot deadlock on a full queue.
        await foreach (var notice in reader.ReadAllAsync().ConfigureAwait(false))
        {
            try
            {
                if (token.IsCancellationRequested) continue;
                lock (_sync)
                {
                    var evidence = notice.Update?.Evidence ?? notice.Detection?.Evidence;
                    if (evidence is not null && evidence.Frame.Sequence < _minimumSequence) continue;
                    if (notice.Update is { } update) ApplyUpdate(update);
                    else if (notice.Detection is { } detection) ApplyReview(detection);
                }
            }
            catch (Exception error) { _diagnostics.Error("Could not apply SMOVision observation.", error); }
            finally { notice.Applied.TrySetResult(); }
        }
    }

    internal void ApplyUpdate(VisionUpdate update)
    {
        if (update.Status == ObservationStatus.Faulted)
        {
            ReportFailure($"SMOVision {update.DetectorId}: {update.Evidence?.Reason ?? "recognition fault"}");
            return;
        }
        if (update.Kind != UpdateKind.ReadingAccepted || update.Reading is null) return;
        if (update.Reading.Value is KingdomId kingdom)
        {
            var name = SmoCatalog.KingdomName(kingdom);
            if (_state.Settings.AutomaticallySwitchKingdoms && _repository.GetKingdoms(_state.Settings).Contains(name)) _state.SetKingdom(name);
            return;
        }
        if (update.Reading.Value is not MoonOccurrence { Moon: { } id } occurrence) return;
        var occurrenceKey = (update.Reading.Evidence.Frame.SessionId, occurrence.OccurrenceId);
        if (_handled.Contains(occurrenceKey)) return;
        var eligible = occurrence.IsHint ? _repository.GetTalkatooCandidates(_state.CurrentKingdom, _state.Settings)
            : _repository.GetCollectionCandidates(_state.CurrentKingdom, _state.Settings);
        var moon = eligible.FirstOrDefault(m => m.Id == id.LocalId && m.Kingdom == SmoCatalog.KingdomName(id.Kingdom));
        if (moon is null) return;
        _handled.Add(occurrenceKey); _handledOrder.Enqueue(occurrenceKey);
        while (_handledOrder.Count > 512) _handled.Remove(_handledOrder.Dequeue());
        if (occurrence.IsHint) _coordinator.ObserveHint(moon);
        else _coordinator.ObserveCollection(moon);
    }

    internal void ApplyReview(Detection detection)
    {
        var hint = detection.Field == Fields.Talkatoo;
        if ((!hint && detection.Field != Fields.MoonCollection) || detection.Status != ObservationStatus.Unreadable ||
            string.IsNullOrWhiteSpace(detection.Evidence.RawText)) return;
        var match = new MoonNameMatcher().Match(detection.Evidence.RawText, SmoCatalog.Language(_state.Settings.InputLanguage),
            SmoCatalog.Kingdom(_state.CurrentKingdom), collectionLocation: !hint);
        if (!match.Ambiguous || match.Candidates.FirstOrDefault()?.Score < .75) return;
        var eligible = hint ? _repository.GetTalkatooCandidates(_state.CurrentKingdom, _state.Settings)
            : _repository.GetCollectionCandidates(_state.CurrentKingdom, _state.Settings);
        var candidates = match.Candidates.Select(c => (moon: eligible.FirstOrDefault(m => m.Id == c.Moon.Id.LocalId && m.Kingdom == SmoCatalog.KingdomName(c.Moon.Id.Kingdom)), score: c.Score))
            .Where(c => c.moon is not null).Take(3).Select(c => (c.moon!, c.score)).ToArray();
        if (candidates.Length == 0) return;
        var signature = $"{_state.CurrentKingdom}|{hint}|{detection.Evidence.RawText}";
        if (!_reviews.Add(signature)) return;
        _reviewOrder.Enqueue(signature);
        while (_reviewOrder.Count > 128) _reviews.Remove(_reviewOrder.Dequeue());
        AmbiguousMatchReceived?.Invoke(this, new(hint ? OcrRegionType.Talkatoo : OcrRegionType.MoonGet, detection.Evidence.RawText, candidates));
    }

    public async ValueTask DisposeAsync()
    {
        _state.Changed -= StateChanged;
        _coordinator.CaptureContextInvalidated -= ContextInvalidated;
        await StopAsync().ConfigureAwait(false);
        _disposed = true;
    }

    private void ReportFailure(string message)
    {
        _diagnostics.Error(message);
        ProcessingFailed?.Invoke(message);
    }

    private sealed record Notice(VisionUpdate? Update = null, Detection? Detection = null)
    {
        public TaskCompletionSource Applied { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    private sealed class Sink(ChannelWriter<Notice> writer) : IAnalysisSink
    {
        public ValueTask ObserveAsync(Detection detection, CancellationToken cancellationToken) =>
            detection.Status == ObservationStatus.Unreadable && detection.Evidence.RawText is not null
                ? EnqueueAsync(new(Detection: detection), cancellationToken) : ValueTask.CompletedTask;
        public ValueTask WriteAsync(VisionUpdate update, CancellationToken cancellationToken) =>
            update.Kind == UpdateKind.ReadingAccepted || update.Status == ObservationStatus.Faulted
                ? EnqueueAsync(new(Update: update), cancellationToken) : ValueTask.CompletedTask;
        private async ValueTask EnqueueAsync(Notice notice, CancellationToken token)
        {
            await writer.WriteAsync(notice, token).ConfigureAwait(false);
            await notice.Applied.Task.WaitAsync(token).ConfigureAwait(false);
        }
    }

    private sealed class PreparedSource(SmoGameSession owner, IFrameSource source) : IFrameSource, IFrameDropStatistics
    {
        public VisionSession Session { get; set; } = null!;
        public FrameSourceInfo Info => source.Info;
        public long DroppedFrames => (source as IFrameDropStatistics)?.DroppedFrames ?? 0;
        public ValueTask InitializeAsync(CancellationToken token = default) => source.InitializeAsync(token);
        public async IAsyncEnumerable<FrameBuffer> ReadFramesAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            FrameLayout? lastLayout = null;
            await foreach (var input in source.ReadFramesAsync(cancellationToken).ConfigureAwait(false))
            {
                FrameBuffer? frame = input;
                try
                {
                    FrameLayout layout;
                    lock (owner._sync)
                    {
                        if (!ReferenceEquals(source, owner._input))
                        {
                            frame = input.Retain(input.Stamp with { Sequence = ++owner._sequence }); input.Dispose();
                        }
                        var crop = owner._crop.Resolve(frame.Width, frame.Height);
                        layout = new(new NormalizedRegion(crop.X / (double)frame.Width, crop.Y / (double)frame.Height,
                            crop.Width / (double)frame.Width, crop.Height / (double)frame.Height), Array.Empty<NormalizedRegion>());
                    }
                    if (lastLayout != layout)
                    {
                        await Session.SetLayoutAsync(layout, cancellationToken).ConfigureAwait(false); lastLayout = layout;
                    }
                    if (Interlocked.Exchange(ref owner._reset, 0) != 0)
                        await Session.ResetAsync(cancellationToken).ConfigureAwait(false);
                    var owned = frame; frame = null;
                    yield return owned;
                }
                finally { frame?.Dispose(); }
            }
        }
        public ValueTask DisposeAsync() => source.DisposeAsync();
    }
}
