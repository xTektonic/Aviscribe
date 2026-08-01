namespace Aviscribe.Core.Capture;

public sealed class CaptureSourceSelection
{
    private readonly Dictionary<CaptureSourceKind, string> _selectedIds = [];

    public CaptureSourceKind Kind { get; private set; } = CaptureSourceKind.VideoDevice;

    public CaptureSourceSelection(
        CaptureSourceKind kind = CaptureSourceKind.VideoDevice,
        IReadOnlyDictionary<CaptureSourceKind, string>? selectedIds = null)
    {
        Kind = kind;
        if (selectedIds == null)
            return;

        foreach (var item in selectedIds.Where(item => !string.IsNullOrWhiteSpace(item.Value)))
            _selectedIds[item.Key] = item.Value;
    }

    public void SetKind(CaptureSourceKind kind) => Kind = kind;

    public void Select(VideoDevice source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Kind = source.Kind;
        _selectedIds[source.Kind] = source.Id;
    }

    public IReadOnlyList<VideoDevice> Filter(IReadOnlyList<VideoDevice> sources) =>
        sources.Where(source => source.Kind == Kind).ToArray();

    public VideoDevice? Restore(IReadOnlyList<VideoDevice> sources)
    {
        var filtered = Filter(sources);
        if (_selectedIds.TryGetValue(Kind, out var id))
        {
            var selected = filtered.FirstOrDefault(source =>
                string.Equals(source.Id, id, StringComparison.Ordinal));
            if (selected != null)
                return selected;
        }

        return filtered.FirstOrDefault();
    }

    public string GetSelectedId(CaptureSourceKind kind) =>
        _selectedIds.TryGetValue(kind, out var id) ? id : string.Empty;

    public IReadOnlyDictionary<CaptureSourceKind, string> Snapshot() =>
        new Dictionary<CaptureSourceKind, string>(_selectedIds);
}
