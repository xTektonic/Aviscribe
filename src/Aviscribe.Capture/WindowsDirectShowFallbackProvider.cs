#if WINDOWS_DIRECTSHOW_FALLBACK
using Accord.Video.DirectShow;
using Aviscribe.Core.Capture;
using System.Collections.ObjectModel;
using System.Runtime.Versioning;

namespace Aviscribe.Capture;

[SupportedOSPlatform("windows")]
internal sealed class WindowsDirectShowFallbackProvider : IVideoProvider
{
    private readonly object _sync = new();
    private IReadOnlyList<VideoDevice> _devices = [];
    private Dictionary<string, DeviceEntry> _entries =
        new(StringComparer.Ordinal);

    public IReadOnlyList<VideoDevice> GetDevices()
    {
        lock (_sync)
        {
            RefreshLocked();
            return _devices;
        }
    }

    public IVideoCapture GetVideoCapture(
        string deviceId,
        string? formatId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        lock (_sync)
        {
            if (!_entries.TryGetValue(deviceId, out var entry))
            {
                RefreshLocked();
                if (!_entries.TryGetValue(deviceId, out entry))
                {
                    throw new InvalidOperationException(
                        "The selected DirectShow compatibility device is no " +
                        "longer available. Refresh the device list and select " +
                        "it again.");
                }
            }

            var capability = SelectCapability(entry.Capabilities, formatId);
            var selectedFormat = capability == null
                ? DefaultFormat
                : ToVideoFormat(capability);
            return new WindowsDirectShowFallbackCapture(
                entry.Device,
                selectedFormat,
                entry.Moniker,
                capability);
        }
    }

    private void RefreshLocked()
    {
        var entries = new FilterInfoCollection(FilterCategory.VideoInputDevice)
            .Cast<FilterInfo>()
            .Select(CreateEntry)
            .GroupBy(item => item.Device.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Device.Id, StringComparer.Ordinal)
            .ToArray();

        _entries = entries.ToDictionary(
            item => item.Device.Id,
            StringComparer.Ordinal);
        _devices = new ReadOnlyCollection<VideoDevice>(
            entries.Select(item => item.Device).ToArray());
    }

    private static DeviceEntry CreateEntry(FilterInfo filter)
    {
        var capabilities = GetCapabilities(filter.MonikerString);
        var device = new VideoDevice
        {
            Id = VideoDeviceKey.Create(
                "windows",
                "directshow-compatibility",
                filter.MonikerString,
                filter.Name),
            Name = filter.Name,
            Backend = "DirectShow compatibility",
            Capabilities = capabilities
                .Select(ToVideoFormat)
                .DistinctBy(item => item.Id)
                .OrderByDescending(item => item.Width * (long)item.Height)
                .ThenByDescending(item => item.FramesPerSecond)
                .ToArray()
        };

        return new DeviceEntry(
            device,
            filter.MonikerString,
            capabilities);
    }

    private static VideoCapabilities[] GetCapabilities(string moniker)
    {
        try
        {
            var source = new VideoCaptureDevice(moniker);
            try
            {
                return source.VideoCapabilities ?? [];
            }
            finally
            {
                source.Stop();
            }
        }
        catch
        {
            return [];
        }
    }

    private static VideoCapabilities? SelectCapability(
        IReadOnlyList<VideoCapabilities> capabilities,
        string? formatId)
    {
        if (!string.IsNullOrWhiteSpace(formatId))
        {
            var requested = capabilities.FirstOrDefault(item =>
                string.Equals(
                    ToVideoFormat(item).Id,
                    formatId,
                    StringComparison.Ordinal));
            if (requested != null)
                return requested;
        }

        return capabilities
            .OrderBy(FormatAspectPenalty)
            .ThenBy(FormatResolutionPenalty)
            .ThenByDescending(item => FrameRate(item))
            .FirstOrDefault();
    }

    private static double FormatAspectPenalty(VideoCapabilities capability)
    {
        var size = capability.FrameSize;
        if (size.Width <= 0 || size.Height <= 0)
            return double.MaxValue;

        return Math.Abs(size.Width / (double)size.Height - 16.0 / 9.0);
    }

    private static long FormatResolutionPenalty(
        VideoCapabilities capability)
    {
        var size = capability.FrameSize;
        return Math.Abs(size.Width - CaptureCropSettings.ReferenceWidth) +
            Math.Abs(size.Height - CaptureCropSettings.ReferenceHeight);
    }

    private static int FrameRate(VideoCapabilities capability)
    {
        return capability.AverageFrameRate > 0
            ? capability.AverageFrameRate
            : capability.MaximumFrameRate;
    }

    private static VideoFormat ToVideoFormat(VideoCapabilities capability)
    {
        var size = capability.FrameSize;
        return new VideoFormat(
            size.Width,
            size.Height,
            "DirectShow",
            FrameRate(capability),
            1,
            $"{capability.BitCount}-bit DirectShow");
    }

    private static readonly VideoFormat DefaultFormat = new(
        0,
        0,
        "DirectShow",
        0,
        1,
        "Driver-selected format");

    private sealed record DeviceEntry(
        VideoDevice Device,
        string Moniker,
        VideoCapabilities[] Capabilities);
}
#endif
