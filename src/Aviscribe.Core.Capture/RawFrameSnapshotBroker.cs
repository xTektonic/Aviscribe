namespace Aviscribe.Core.Capture;

/// <summary>
/// Coordinates one raw-frame calibration request at a time. A newer request
/// cancels the older request. Offered frames are cloned, so this broker never
/// consumes the capture pipeline's frame ownership.
/// </summary>
public sealed class RawFrameSnapshotBroker : IDisposable
{
    private readonly object _sync = new();
    private TaskCompletionSource<VideoFrame>? _pending;
    private bool _disposed;

    public async Task<VideoFrame> RequestAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout));

        var request = new TaskCompletionSource<VideoFrame>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<VideoFrame>? superseded;
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            superseded = _pending;
            _pending = request;
        }
        superseded?.TrySetCanceled();

        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linkedCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                timeoutCancellation.Token);
        using var registration = linkedCancellation.Token.Register(() =>
            request.TrySetCanceled(linkedCancellation.Token));

        try
        {
            return await request.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
            when (timeoutCancellation.IsCancellationRequested &&
                  !cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                "No capture frame arrived before the snapshot timeout.");
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_pending, request))
                    _pending = null;
            }
        }
    }

    public bool Offer(VideoFrame rawFrame)
    {
        ArgumentNullException.ThrowIfNull(rawFrame);

        TaskCompletionSource<VideoFrame>? request;
        lock (_sync)
        {
            request = _pending;
            if (request != null)
                _pending = null;
        }
        if (request == null)
            return false;

        var snapshot = rawFrame.Clone();
        if (request.TrySetResult(snapshot))
            return true;

        snapshot.Dispose();
        return false;
    }

    public void Cancel(Exception? reason = null)
    {
        TaskCompletionSource<VideoFrame>? request;
        lock (_sync)
        {
            request = _pending;
            _pending = null;
        }

        if (reason is not null and not OperationCanceledException)
            request?.TrySetException(reason);
        else
            request?.TrySetCanceled();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        Cancel(new ObjectDisposedException(nameof(RawFrameSnapshotBroker)));
    }
}
