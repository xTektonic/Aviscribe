using Aviscribe.Core.Capture;
using FlashCap;
using System.Collections.ObjectModel;

namespace Aviscribe.Capture;

public sealed class FlashCapVideoProvider : IVideoProvider
{
    private readonly object _sync = new();
    private readonly CaptureDevices _captureDevices = new();
    private IReadOnlyList<VideoDevice> _devices = [];
    private Dictionary<string, DescriptorEntry> _descriptors =
        new(StringComparer.Ordinal);

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
            .Where(item => item.Device.Capabilities.Count > 0)
            .GroupBy(LinuxPhysicalDeviceKey, StringComparer.Ordinal)
            .Select(group => group
                .OrderByDescending(item => item.Device.Capabilities.Count)
                .First())
            .GroupBy(item => item.Device.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.Device.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(item => item.Device.Id, StringComparer.Ordinal)
            .ToArray();

        _descriptors = entries.ToDictionary(
            item => item.Device.Id,
            StringComparer.Ordinal);

        _devices = new ReadOnlyCollection<VideoDevice>(
            entries
                .Select(item => item.Device)
                .OrderBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Id, StringComparer.Ordinal)
                .ToArray());
    }

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

    private static string LinuxPhysicalDeviceKey(DescriptorEntry entry)
    {
        if (!OperatingSystem.IsLinux() ||
            entry.Descriptor.DeviceType != DeviceTypes.V4L2)
            return entry.Device.Id;

        var identity = Convert.ToString(
            entry.Descriptor.Identity,
            System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(identity))
            return entry.Device.Id;

        var deviceName = Path.GetFileName(identity.Trim());
        if (!deviceName.StartsWith("video", StringComparison.Ordinal))
            return entry.Device.Id;

        try
        {
            var sysfsDevice = new DirectoryInfo(
                Path.Combine("/sys/class/video4linux", deviceName, "device"));
            var physicalDevice = sysfsDevice.ResolveLinkTarget(returnFinalTarget: true);
            return physicalDevice?.FullName ?? entry.Device.Id;
        }
        catch (IOException)
        {
            return entry.Device.Id;
        }
        catch (UnauthorizedAccessException)
        {
            return entry.Device.Id;
        }
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
