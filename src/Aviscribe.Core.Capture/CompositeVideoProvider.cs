namespace Aviscribe.Core.Capture;

public sealed class CompositeVideoProvider : IVideoProvider
{
    private readonly IReadOnlyList<IVideoProvider> _providers;
    private readonly object _sync = new();
    private Dictionary<string, IVideoProvider> _owners = new(StringComparer.Ordinal);
    private IReadOnlyList<VideoDevice> _sources = [];

    public CompositeVideoProvider(params IVideoProvider[] providers)
    {
        ArgumentNullException.ThrowIfNull(providers);
        if (providers.Length == 0)
            throw new ArgumentException("At least one capture provider is required.", nameof(providers));

        _providers = providers;
    }

    public IReadOnlyList<VideoDevice> GetDevices()
    {
        lock (_sync)
        {
            var sources = new List<(VideoDevice Source, IVideoProvider Owner)>();
            foreach (var provider in _providers)
                sources.AddRange(provider.GetDevices().Select(source => (source, provider)));
            ApplySources(sources.Select(item => item.Source), sources);
            return _sources;
        }
    }

    public IVideoCapture GetVideoCapture(string deviceId, string? formatId = null)
    {
        IVideoProvider owner;
        lock (_sync)
        {
            if (!_owners.TryGetValue(deviceId, out owner!))
                throw new InvalidOperationException("The selected capture source is no longer available. Refresh the source list and select it again.");
        }

        return owner.GetVideoCapture(deviceId, formatId);
    }

    public async ValueTask<IReadOnlyList<VideoDevice>> RefreshAsync(CancellationToken cancellationToken = default)
    {
        var sources = new List<(VideoDevice Source, IVideoProvider Owner)>();
        foreach (var provider in _providers)
        {
            var providerSources = await provider.RefreshAsync(cancellationToken).ConfigureAwait(false);
            sources.AddRange(providerSources.Select(source => (source, provider)));
        }

        lock (_sync)
        {
            ApplySources(sources.Select(item => item.Source), sources);
            return _sources;
        }
    }

    public ValueTask<IVideoCapture> OpenCaptureAsync(string deviceId, string? formatId = null, CancellationToken cancellationToken = default)
    {
        return OpenCaptureAsync(
            deviceId,
            formatId,
            CaptureOpenOptions.Default,
            cancellationToken);
    }

    public ValueTask<IVideoCapture> OpenCaptureAsync(
        string deviceId,
        string? formatId,
        CaptureOpenOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        cancellationToken.ThrowIfCancellationRequested();
        IVideoProvider owner;
        lock (_sync)
        {
            if (!_owners.TryGetValue(deviceId, out owner!))
                throw new InvalidOperationException("The selected capture source is no longer available. Refresh the source list and select it again.");
        }

        return owner.OpenCaptureAsync(deviceId, formatId, options, cancellationToken);
    }

    private void ApplySources(
        IEnumerable<VideoDevice> sources,
        IEnumerable<(VideoDevice Source, IVideoProvider Owner)> ownedSources)
    {
        var sourceArray = sources
            .GroupBy(source => source.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(source => source.Kind)
            .ThenBy(source => source.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        _owners = ownedSources
            .GroupBy(item => item.Source.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().Owner, StringComparer.Ordinal);

        _sources = sourceArray;
    }
}
