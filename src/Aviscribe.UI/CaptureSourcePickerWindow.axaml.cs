using Avalonia.Controls;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;

namespace Aviscribe.UI;

public partial class CaptureSourcePickerWindow : Window
{
    private readonly IVideoProvider _provider;
    private readonly CaptureOpenOptions _openOptions;
    private readonly IAppDiagnostics _diagnostics;
    private readonly CancellationToken _applicationCancellation;
    private readonly ListBox _sources;
    private readonly Button _chooseButton;
    private readonly Button _cancelButton;
    private readonly TextBlock _statusText;
    private CancellationTokenSource? _selectionCancellation;
    private bool _busy;
    private bool _closed;
    private bool _closeAfterCancellation;

    public CaptureSourcePickerWindow()
        : this(
            new DesignVideoProvider(),
            [],
            null,
            CaptureOpenOptions.Default,
            NullAppDiagnostics.Instance,
            CancellationToken.None)
    {
    }

    public CaptureSourcePickerWindow(
        IVideoProvider provider,
        IReadOnlyList<VideoDevice> sources,
        string? selectedId,
        CaptureOpenOptions openOptions,
        IAppDiagnostics diagnostics,
        CancellationToken applicationCancellation)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(openOptions);
        ArgumentNullException.ThrowIfNull(diagnostics);

        InitializeComponent();
        _provider = provider;
        _openOptions = openOptions;
        _diagnostics = diagnostics;
        _applicationCancellation = applicationCancellation;
        _sources = this.GetControl<ListBox>("lstCaptureSources");
        _chooseButton = this.GetControl<Button>("btnChoose");
        _cancelButton = this.GetControl<Button>("btnCancel");
        _statusText = this.GetControl<TextBlock>("txtPickerStatus");

        var items = sources
            .OrderBy(source => source.Kind)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .Select(source => new CaptureSourceItem(source))
            .ToArray();
        _sources.ItemsSource = items;
        _sources.SelectedItem = items.FirstOrDefault(item =>
                item.Device.IsAvailable &&
                string.Equals(item.Device.Id, selectedId, StringComparison.Ordinal)) ??
            items.FirstOrDefault(item => item.Device.IsAvailable);

        _statusText.Text = items.Length == 0
            ? "No compatible capture sources were found."
            : string.Empty;
        _chooseButton.Click += async (_, _) => await ChooseAsync();
        _cancelButton.Click += (_, _) => Cancel();
        _sources.DoubleTapped += async (_, _) => await ChooseAsync();
        _sources.SelectionChanged += (_, _) => UpdateChooseState();
        Closed += (_, _) =>
        {
            _closed = true;
            _selectionCancellation?.Cancel();
        };
        UpdateChooseState();
    }

    private async Task ChooseAsync()
    {
        if (_busy ||
            _sources.SelectedItem is not CaptureSourceItem item ||
            !item.Device.IsAvailable)
        {
            return;
        }

        if (!item.Device.RequiresInteractiveSelection)
        {
            Close(new CaptureSourcePickerResult(item.Device, null));
            return;
        }

        SetBusy(true);
        _statusText.Text = "Waiting for the desktop window picker…";
        _selectionCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _applicationCancellation);
        IVideoCapture? preparedCapture = null;
        try
        {
            preparedCapture = await _provider.OpenCaptureAsync(
                item.Device.Id,
                formatId: null,
                options: _openOptions,
                cancellationToken: _selectionCancellation.Token);
            var result = new CaptureSourcePickerResult(
                preparedCapture.Device,
                preparedCapture);
            preparedCapture = null;
            if (_closed)
                await result.DisposeAsync();
            else
                Close(result);
        }
        catch (OperationCanceledException)
        {
            if (_applicationCancellation.IsCancellationRequested ||
                _closeAfterCancellation ||
                _closed)
            {
                if (!_closed)
                    Close(null);
            }
            else
            {
                _statusText.Text = "Window selection was cancelled. Choose a source to try again.";
                SetBusy(false);
            }
        }
        catch (Exception ex)
        {
            _diagnostics.Error("Could not prepare the selected capture source.", ex);
            _statusText.Text = ex.Message;
            SetBusy(false);
        }
        finally
        {
            if (preparedCapture != null)
                await preparedCapture.DisposeAsync();
            _selectionCancellation?.Dispose();
            _selectionCancellation = null;
            _closeAfterCancellation = false;
        }
    }

    private void Cancel()
    {
        if (!_busy)
        {
            Close(null);
            return;
        }

        _closeAfterCancellation = true;
        _cancelButton.IsEnabled = false;
        _statusText.Text = "Cancelling the desktop window picker…";
        _selectionCancellation?.Cancel();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        _sources.IsEnabled = !busy;
        _cancelButton.IsEnabled = true;
        UpdateChooseState();
    }

    private void UpdateChooseState()
    {
        _chooseButton.IsEnabled = !_busy &&
            _sources.SelectedItem is CaptureSourceItem { Device.IsAvailable: true };
    }

    private sealed record CaptureSourceItem(VideoDevice Device)
    {
        public string KindLabel =>
            Device.Kind == CaptureSourceKind.Window ? "Window" : "Video device";

        public string Detail => Device.IsAvailable
            ? Device.Backend
            : Device.UnavailableReason;
    }
}

public sealed class CaptureSourcePickerResult : IAsyncDisposable
{
    private IVideoCapture? _preparedCapture;

    public CaptureSourcePickerResult(
        VideoDevice device,
        IVideoCapture? preparedCapture)
    {
        Device = device;
        _preparedCapture = preparedCapture;
    }

    public VideoDevice Device { get; }

    public IVideoCapture? TakePreparedCapture() =>
        Interlocked.Exchange(ref _preparedCapture, null);

    public async ValueTask DisposeAsync()
    {
        var capture = TakePreparedCapture();
        if (capture != null)
            await capture.DisposeAsync();
    }
}
