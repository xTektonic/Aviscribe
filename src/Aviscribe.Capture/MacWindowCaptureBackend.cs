using Aviscribe.Core.Capture;
using System.Runtime.InteropServices;

namespace Aviscribe.Capture;

internal sealed class MacWindowCaptureBackend : IWindowCaptureBackend
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint WindowListOptionAll = 0;
    private static readonly Lazy<nint> CoreGraphicsLibrary =
        new(() => NativeLibrary.Load(CoreGraphics));

    public string Name => "macOS ScreenCaptureKit";

    public bool TryRequestAccess() =>
        !CGPreflightScreenCaptureAccess() && CGRequestScreenCaptureAccess();

    public IReadOnlyList<WindowCaptureTarget> EnumerateTargets()
    {
        if (!CGPreflightScreenCaptureAccess())
            return [Unavailable("Allow Aviscribe under System Settings > Privacy & Security > Screen & System Audio Recording, then restart the app.")];

        var descriptions = CGWindowListCopyWindowInfo(WindowListOptionAll, 0);
        if (descriptions == 0)
            return [Unavailable("macOS did not return any capturable windows.")];

        try
        {
            var targets = new List<WindowCaptureTarget>();
            var count = CFArrayGetCount(descriptions);
            for (nint index = 0; index < count; index++)
            {
                var dictionary = CFArrayGetValueAtIndex(descriptions, index);
                if (!TryReadInt(dictionary, "kCGWindowLayer", out var layer) || layer != 0 ||
                    !TryReadInt(dictionary, "kCGWindowNumber", out var windowNumber) ||
                    !TryReadBounds(dictionary, out var bounds) || bounds.Width < 320 || bounds.Height < 180)
                    continue;

                var owner = ReadString(dictionary, "kCGWindowOwnerName");
                var title = ReadString(dictionary, "kCGWindowName");
                if (string.IsNullOrWhiteSpace(owner) && string.IsNullOrWhiteSpace(title))
                    continue;

                var label = string.IsNullOrWhiteSpace(title) ? owner : $"{owner} - {title}";
                var identity = $"{owner}\n{title}";
                targets.Add(new WindowCaptureTarget(
                    VideoDeviceKey.Create("macos", "window", identity, label),
                    label,
                    (nint)windowNumber,
                    (int)Math.Round(bounds.Width),
                    (int)Math.Round(bounds.Height)));
            }

            var result = targets
                .GroupBy(target => target.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return result.Length > 0
                ? result
                : [Unavailable("No capturable macOS windows were found. Open the game or capture application, then refresh.")];
        }
        finally
        {
            CFRelease(descriptions);
        }
    }

    public IWindowCaptureSession OpenSession(WindowCaptureTarget target) =>
        new MacScreenCaptureSession(checked((uint)target.NativeHandle));

    private static WindowCaptureTarget Unavailable(string reason) =>
        new("window:macos:permission", reason, 0, 0, 0, false, reason);

    private static string ReadString(nint dictionary, string keyName)
    {
        if (!TryGetValue(dictionary, keyName, out var value) || value == 0)
            return string.Empty;
        var length = CFStringGetLength(value);
        if (length <= 0)
            return string.Empty;
        var capacity = checked((int)(length * 4 + 1));
        var buffer = new byte[capacity];
        return CFStringGetCString(value, buffer, capacity, 0x08000100)
            ? System.Text.Encoding.UTF8.GetString(buffer.AsSpan(0, Array.IndexOf(buffer, (byte)0)))
            : string.Empty;
    }

    private static bool TryReadInt(nint dictionary, string keyName, out long number)
    {
        number = 0;
        return TryGetValue(dictionary, keyName, out var value) &&
            value != 0 &&
            CFNumberGetValue(value, 4, out number);
    }

    private static bool TryReadBounds(nint dictionary, out CGRect bounds)
    {
        bounds = default;
        return TryGetValue(dictionary, "kCGWindowBounds", out var value) &&
            value != 0 &&
            CGRectMakeWithDictionaryRepresentation(value, out bounds);
    }

    private static bool TryGetValue(nint dictionary, string keyName, out nint value) =>
        CFDictionaryGetValueIfPresent(dictionary, GetCoreGraphicsConstant(keyName), out value);

    private static nint GetCoreGraphicsConstant(string name)
    {
        var export = NativeLibrary.GetExport(CoreGraphicsLibrary.Value, name);
        return Marshal.ReadIntPtr(export);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CGPoint { public double X; public double Y; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CGSize { public double Width; public double Height; }
    [StructLayout(LayoutKind.Sequential)]
    private struct CGRect { public CGPoint Origin; public CGSize Size; public double Width => Size.Width; public double Height => Size.Height; }

    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGPreflightScreenCaptureAccess();
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRequestScreenCaptureAccess();
    [DllImport(CoreGraphics)]
    private static extern nint CGWindowListCopyWindowInfo(uint option, uint relativeToWindow);
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRectMakeWithDictionaryRepresentation(nint dictionary, out CGRect bounds);
    [DllImport(CoreFoundation)]
    private static extern nint CFArrayGetCount(nint array);
    [DllImport(CoreFoundation)]
    private static extern nint CFArrayGetValueAtIndex(nint array, nint index);
    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFDictionaryGetValueIfPresent(nint dictionary, nint key, out nint value);
    [DllImport(CoreFoundation)]
    private static extern nint CFStringGetLength(nint value);
    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFStringGetCString(nint value, byte[] buffer, nint bufferSize, uint encoding);
    [DllImport(CoreFoundation)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CFNumberGetValue(nint number, int type, out long value);
    [DllImport(CoreFoundation)]
    private static extern void CFRelease(nint value);
}
