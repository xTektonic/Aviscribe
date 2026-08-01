using Avalonia.Controls;
using Aviscribe.Core.Capture;

namespace Aviscribe.UI;

public partial class CaptureSourcePickerWindow : Window
{
    private readonly ListBox _sources;

    public CaptureSourcePickerWindow()
        : this([], null)
    {
    }

    public CaptureSourcePickerWindow(
        IReadOnlyList<VideoDevice> sources,
        string? selectedId)
    {
        InitializeComponent();
        _sources = this.GetControl<ListBox>("lstCaptureSources");
        var items = sources
            .OrderBy(source => source.Kind)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .Select(source => new CaptureSourceItem(source))
            .ToArray();
        _sources.ItemsSource = items;
        _sources.SelectedItem = items.FirstOrDefault(item =>
            string.Equals(item.Device.Id, selectedId, StringComparison.Ordinal)) ??
            items.FirstOrDefault();

        this.GetControl<TextBlock>("txtEmptySources").IsVisible = items.Length == 0;
        this.GetControl<Button>("btnChoose").IsEnabled = items.Length > 0;
        this.GetControl<Button>("btnChoose").Click += (_, _) => Choose();
        this.GetControl<Button>("btnCancel").Click += (_, _) => Close(null);
        _sources.DoubleTapped += (_, _) => Choose();
    }

    private void Choose()
    {
        if (_sources.SelectedItem is CaptureSourceItem item)
            Close(item.Device);
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
