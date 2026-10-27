using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using SMOVision.Capture;
using SMOVision.Core.Capture;
using AppSourceKind = Aviscribe.Core.Capture.CaptureSourceKind;

namespace Aviscribe.Core.Integration;

/// <summary>UI compatibility boundary; all native capture belongs to SMOVision.</summary>
public sealed class SmoVideoProvider : IVideoProvider
{
    private readonly CaptureProvider[] _providers = [CaptureProvider.Create()];
    private readonly Dictionary<string, (CaptureProvider Provider, VideoDevice Device)> _sources = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SmoVideoProvider(IAppDiagnostics diagnostics)
    {
        foreach (var provider in _providers) provider.Diagnostic += d => diagnostics.Information($"SMOVision capture: {d.Message}");
    }

    public static VideoDevice ConvertSource(CaptureSource source) => new()
    {
        Id = source.Id, Name = source.Name, Backend = source.Backend,
        Kind = source.Kind == SMOVision.Capture.CaptureSourceKind.Window ? AppSourceKind.Window : AppSourceKind.VideoDevice,
        IsAvailable = source.IsAvailable, UnavailableReason = source.UnavailableReason ?? "",
        RequiresInteractiveSelection = source.RequiresInteractiveSelection,
        Capabilities = source.Formats.Select(f => new VideoFormat(f.Width, f.Height, f.PixelFormat, f.FrameRateNumerator, f.FrameRateDenominator) { SourceFormatId = f.Id }).ToArray()
    };

    public IReadOnlyList<VideoDevice> GetDevices() => RefreshAsync().AsTask().GetAwaiter().GetResult();
    public IVideoCapture GetVideoCapture(string deviceId, string? formatId = null) => OpenCaptureAsync(deviceId, formatId).AsTask().GetAwaiter().GetResult();
    public async ValueTask<IReadOnlyList<VideoDevice>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var found = new Dictionary<string, (CaptureProvider, VideoDevice)>(StringComparer.Ordinal);
            foreach (var provider in _providers)
                foreach (var source in await provider.RefreshAsync(cancellationToken).ConfigureAwait(false))
                    found[source.Id] = (provider, ConvertSource(source));
            _sources.Clear();
            foreach (var pair in found) _sources.Add(pair.Key, pair.Value);
            return _sources.Values.Select(x => x.Device).ToArray();
        }
        finally { _gate.Release(); }
    }

    public ValueTask<IVideoCapture> OpenCaptureAsync(string deviceId, string? formatId = null, CancellationToken cancellationToken = default) =>
        OpenCaptureAsync(deviceId, formatId, CaptureOpenOptions.Default, cancellationToken);

    public async ValueTask<IVideoCapture> OpenCaptureAsync(string deviceId, string? formatId, CaptureOpenOptions options, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_sources.TryGetValue(deviceId, out var source)) throw new IOException("Refresh the capture sources and select the source again.");
            VideoFormat? selected = null;
            if (formatId is not null) selected = source.Device.Capabilities.FirstOrDefault(f => f.Id == formatId)
                ?? throw new ArgumentException("The selected format is no longer available.", nameof(formatId));
            var captureOptions = new CaptureOptions(selected?.Id, options.ParentWindowIdentifier, "gameplay");
            var frames = await source.Provider.OpenAsync(deviceId, captureOptions, cancellationToken).ConfigureAwait(false);
            return new SmoVideoCapture(source.Device, selected, frames,
                async token => await source.Provider.OpenAsync(deviceId, captureOptions, token).ConfigureAwait(false));
        }
        finally { _gate.Release(); }
    }
}

public sealed class SmoVideoCapture : IVideoCapture
{
    private readonly Func<CancellationToken, ValueTask<IFrameSource>> _reopen;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IFrameSource? _source;
    private CancellationTokenSource? _cancel;
    private Task? _pump;
    private bool _disposed;
    public VideoDevice Device { get; }
    public VideoFormat SelectedFormat { get; private set; }
    public CaptureState State { get; private set; } = CaptureState.Stopped;
    public event Action<VideoFrame>? FrameReceived;
    public event EventHandler<CaptureStateChangedEventArgs>? StateChanged;
    public event EventHandler<CaptureErrorEventArgs>? CaptureFailed;

    public SmoVideoCapture(VideoDevice device, VideoFormat? format, IFrameSource source, Func<CancellationToken, ValueTask<IFrameSource>> reopen)
    {
        Device = device; SelectedFormat = format ?? new(0, 0, "BGR24", 0, 1);
        _source = source; _reopen = reopen;
    }

    private void SetState(CaptureState value)
    { var previous = State; State = value; StateChanged?.Invoke(this, new(previous, value)); }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_pump is { IsCompleted: false }) return;
            if (_pump is not null) await StopCoreAsync().ConfigureAwait(false);
            SetState(CaptureState.Starting);
            _source ??= await _reopen(cancellationToken).ConfigureAwait(false);
            _cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var source = _source;
            var token = _cancel.Token;
            _pump = Task.Run(() => PumpAsync(source, ready, token), CancellationToken.None);
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await StopCoreAsync().ConfigureAwait(false);
            SetState(CaptureState.Faulted); throw;
        }
        finally { _gate.Release(); }
    }

    private async Task PumpAsync(IFrameSource source, TaskCompletionSource ready, CancellationToken token)
    {
        try
        {
            await source.InitializeAsync(token).ConfigureAwait(false);
            await foreach (var buffer in source.ReadFramesAsync(token).ConfigureAwait(false))
            {
                using var frame = new VideoFrame(buffer);
                SelectedFormat = SelectedFormat with { Width = buffer.Width, Height = buffer.Height };
                if (State == CaptureState.Starting)
                {
                    SetState(CaptureState.Running); ready.TrySetResult();
                }
                if (FrameReceived is { } handlers)
                    foreach (Action<VideoFrame> handler in handlers.GetInvocationList())
                    {
                        var lease = frame.Clone();
                        try { handler(lease); } catch { lease.Dispose(); throw; }
                    }
            }
            if (!token.IsCancellationRequested) throw new IOException("The capture source stopped producing frames.");
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { ready.TrySetCanceled(token); }
        catch (Exception error)
        {
            SetState(CaptureState.Faulted); ready.TrySetException(error);
            CaptureFailed?.Invoke(this, new(error.Message, error, true));
        }
        finally { ready.TrySetCanceled(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try { await StopCoreAsync().ConfigureAwait(false); }
        finally { _gate.Release(); }
    }
    private async Task StopCoreAsync()
    {
        if (_disposed) return;
        SetState(CaptureState.Stopping);
        _cancel?.Cancel();
        try { if (_pump is not null) await _pump.ConfigureAwait(false); }
        finally
        {
            _pump = null; _cancel?.Dispose(); _cancel = null;
            var source = _source; _source = null;
            if (source is not null) await source.DisposeAsync().ConfigureAwait(false);
            SetState(CaptureState.Stopped);
        }
    }
    public async ValueTask DisposeAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try { if (!_disposed) { await StopCoreAsync().ConfigureAwait(false); _disposed = true; SetState(CaptureState.Disposed); } }
        finally { _gate.Release(); }
    }
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}
