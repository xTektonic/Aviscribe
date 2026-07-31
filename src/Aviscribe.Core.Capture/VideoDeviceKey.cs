using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Aviscribe.Core.Capture;

public static class VideoDeviceKey
{
    public static string Create(
        string platform,
        string backend,
        object? nativeIdentity,
        string fallbackName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(platform);
        ArgumentException.ThrowIfNullOrWhiteSpace(backend);

        var identity = Convert.ToString(
            nativeIdentity,
            CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(identity))
            identity = fallbackName;

        identity = identity.Trim().Normalize(NormalizationForm.FormKC);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        var fingerprint = Convert.ToHexString(hash.AsSpan(0, 12))
            .ToLowerInvariant();
        return $"{platform.Trim().ToLowerInvariant()}:" +
            $"{backend.Trim().ToLowerInvariant()}:{fingerprint}";
    }

    public static string CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return "windows";
        if (OperatingSystem.IsMacOS())
            return "macos";
        if (OperatingSystem.IsLinux())
            return "linux";
        return "unknown";
    }
}
