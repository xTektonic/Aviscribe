using Aviscribe.Core.Capture;
using FlashCap;
using System.Collections.ObjectModel;
#if WINDOWS_DIRECTSHOW_FALLBACK
using System.Runtime.Versioning;
#endif

namespace Aviscribe.Capture;

public sealed class FlashCapVideoProvider : IVideoProvider
{
    private readonly object _sync = new();
    private readonly CaptureDevices _captureDevices = new();
    private IReadOnlyList<VideoDevice> _devices = [];
    private Dictionary<string, DescriptorEntry> _descriptors =
        new(StringComparer.Ordinal);
#if WINDOWS_DIRECTSHOW_FALLBACK
    private WindowsDirectShowFallbackProvider? _windowsFallback;
    private HashSet<string> _fallbackDeviceIds =
        new(StringComparer.Ordinal);
#endif

    public IReadOnlyList<VideoDevice> GetDevices()
    {
        return Refresh();
    }

    public IVideoCapture GetVideoCapture(
        string deviceId,
        string? formatId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);

        DescriptorEntry entry;
        lock (_sync)
        {
            if (!_descriptors.TryGetValue(deviceId, out entry!))
            {
                RefreshLocked();
                if (!_descriptors.TryGetValue(deviceId, out entry!))
                {
#if WINDOWS_DIRECTSHOW_FALLBACK
                    if (OperatingSystem.IsWindows() &&
                        _fallbackDeviceIds.Contains(deviceId))
                    {
                        return OpenWindowsFallbackCapture(
                            deviceId,
                            formatId);
                    }
#endif
                    throw new InvalidOperationException(
                        "The selected capture device is no longer available. " +
                        "Refresh the device list and select it again.");
                }
            }
        }

        var characteristics = SelectCharacteristics(entry.Descriptor, formatId);
        var selectedFormat = ToVideoFormat(characteristics);
        return new FlashCapVideoCapture(
            entry.Device,
            selectedFormat,
            entry.Descriptor,
            characteristics);
    }

    public ValueTask<IReadOnlyList<VideoDevice>> RefreshAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Refresh());
    }

    public ValueTask<IVideoCapture> OpenCaptureAsync(
        string deviceId,
        string? formatId = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(GetVideoCapture(deviceId, formatId));
    }

    private IReadOnlyList<VideoDevice> Refresh()
    {
        lock (_sync)
        {
            RefreshLocked();
            return _devices;
        }
    }

    private void RefreshLocked()
    {
        var entries = _captureDevices
            .EnumerateDescriptors()
            .Where(IsSupportedBackend)
            .Select(CreateEntry)
            .GroupBy(item => item.Device.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Device.Id, StringComparer.Ordinal)
            .ToArray();

        _descriptors = entries.ToDictionary(
            item => item.Device.Id,
            StringComparer.Ordinal);

        IEnumerable<VideoDevice> devices =
            entries.Select(item => item.Device);
#if WINDOWS_DIRECTSHOW_FALLBACK
        _fallbackDeviceIds = new HashSet<string>(StringComparer.Ordinal);
        if (OperatingSystem.IsWindows())
        {
            // FlashCap 1.11 requires DirectShow's optional DevicePath
            // property. OBS Virtual Camera can omit it, so merge only the
            // filters that FlashCap did not already expose.
            var flashCapNames = entries
                .Select(item => item.Device.Name.Trim())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var missingFallbackDevices = GetWindowsFallbackDevices()
                .Where(item => !flashCapNames.Contains(item.Name.Trim()))
                .ToArray();
            _fallbackDeviceIds = missingFallbackDevices
                .Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            devices = devices.Concat(missingFallbackDevices);
        }
#endif

        _devices = new ReadOnlyCollection<VideoDevice>(
            devices
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray());
    }

#if WINDOWS_DIRECTSHOW_FALLBACK
    [SupportedOSPlatform("windows")]
    private IReadOnlyList<VideoDevice> GetWindowsFallbackDevices()
    {
        _windowsFallback ??= new WindowsDirectShowFallbackProvider();
        try
        {
            return _windowsFallback.GetDevices();
        }
        catch
        {
            return [];
        }
    }

    [SupportedOSPlatform("windows")]
    private IVideoCapture OpenWindowsFallbackCapture(
        string deviceId,
        string? formatId)
    {
        _windowsFallback ??= new WindowsDirectShowFallbackProvider();
        return _windowsFallback.GetVideoCapture(deviceId, formatId);
    }
#endif

    private static DescriptorEntry CreateEntry(
        CaptureDeviceDescriptor descriptor)
    {
        var backend = descriptor.DeviceType.ToString();
        var device = new VideoDevice
        {
            Id = VideoDeviceKey.Create(
                VideoDeviceKey.CurrentPlatform(),
                backend,
                descriptor.Identity,
                descriptor.Name),
            Name = string.IsNullOrWhiteSpace(descriptor.Name)
                ? descriptor.Description
                : descriptor.Name,
            Backend = backend,
            Capabilities = descriptor.Characteristics
                .Where(item => item.PixelFormat != PixelFormats.Unknown)
                .Select(ToVideoFormat)
                .DistinctBy(item => item.Id)
                .OrderByDescending(item => item.Width * (long)item.Height)
                .ThenByDescending(item => item.FramesPerSecond)
                .ToArray()
        };

        return new DescriptorEntry(device, descriptor);
    }

    private static bool IsSupportedBackend(CaptureDeviceDescriptor descriptor)
    {
        return descriptor.DeviceType switch
        {
            DeviceTypes.DirectShow => OperatingSystem.IsWindows(),
            DeviceTypes.AVFoundation => OperatingSystem.IsMacOS(),
            DeviceTypes.V4L2 => OperatingSystem.IsLinux(),
            _ => false
        };
    }

    private static VideoCharacteristics SelectCharacteristics(
        CaptureDeviceDescriptor descriptor,
        string? formatId)
    {
        var supported = descriptor.Characteristics
            .Where(item => item.PixelFormat != PixelFormats.Unknown)
            .ToArray();
        if (supported.Length == 0)
        {
            throw new InvalidOperationException(
                $"{descriptor.Name} did not report a FlashCap-compatible " +
                "capture format. Disconnect other camera applications and " +
                "refresh the device list.");
        }

        if (!string.IsNullOrWhiteSpace(formatId))
        {
            var requested = supported.FirstOrDefault(item =>
                string.Equals(
                    ToVideoFormat(item).Id,
                    formatId,
                    StringComparison.Ordinal));
            if (requested != null)
                return requested;
        }

        return supported
            .OrderBy(FormatAspectPenalty)
            .ThenBy(FormatResolutionPenalty)
            .ThenByDescending(item => FramesPerSecond(item))
            .ThenBy(item => PixelFormatPenalty(item.PixelFormat))
            .First();
    }

    private static double FormatAspectPenalty(VideoCharacteristics format)
    {
        if (format.Width <= 0 || format.Height <= 0)
            return double.MaxValue;

        return Math.Abs(format.Width / (double)format.Height - 16.0 / 9.0);
    }

    private static long FormatResolutionPenalty(VideoCharacteristics format)
    {
        var widthDelta = format.Width - CaptureCropSettings.ReferenceWidth;
        var heightDelta = format.Height - CaptureCropSettings.ReferenceHeight;
        return Math.Abs(widthDelta) + Math.Abs(heightDelta);
    }

    private static int PixelFormatPenalty(PixelFormats pixelFormat)
    {
        return pixelFormat switch
        {
            PixelFormats.JPEG => 0,
            PixelFormats.PNG => 1,
            PixelFormats.RGB24 => 2,
            PixelFormats.RGB32 => 3,
            _ => 4
        };
    }

    private static VideoFormat ToVideoFormat(VideoCharacteristics value)
    {
        return new VideoFormat(
            value.Width,
            value.Height,
            value.PixelFormat.ToString(),
            value.FramesPerSecond.Numerator,
            value.FramesPerSecond.Denominator,
            value.Description ?? string.Empty);
    }

    private static double FramesPerSecond(VideoCharacteristics value)
    {
        return value.FramesPerSecond.Denominator == 0
            ? 0
            : value.FramesPerSecond.Numerator /
                (double)value.FramesPerSecond.Denominator;
    }

    private sealed record DescriptorEntry(
        VideoDevice Device,
        CaptureDeviceDescriptor Descriptor);
}
