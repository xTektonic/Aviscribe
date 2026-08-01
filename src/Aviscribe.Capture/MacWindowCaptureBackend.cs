using Aviscribe.Core.Capture;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace Aviscribe.Capture;

internal sealed class MacWindowCaptureBackend : IWindowCaptureBackend
{
    private const string CoreGraphics = "/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint WindowListOptionAll = 0;
    private const uint WindowListOptionIncludingWindow = 1u << 3;
    private const uint ImageBoundsIgnoreFraming = 1u << 0;
    private const uint ImageBestResolution = 1u << 3;
    private const uint PremultipliedFirstLittleEndian = 2u | (2u << 12);
    private static readonly Lazy<nint> CoreGraphicsLibrary =
        new(() => NativeLibrary.Load(CoreGraphics));

    public string Name => "macOS CoreGraphics";

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

    public Mat Capture(WindowCaptureTarget target)
    {
        var image = CGWindowListCreateImage(
            new CGRect
            {
                Origin = new CGPoint
                {
                    X = double.PositiveInfinity,
                    Y = double.PositiveInfinity
                }
            },
            WindowListOptionIncludingWindow,
            (uint)target.NativeHandle,
            ImageBoundsIgnoreFraming | ImageBestResolution);
        if (image == 0)
            throw new InvalidOperationException("macOS could not capture the selected window. It may be minimized, closed, or protected.");

        try
        {
            var width = checked((int)CGImageGetWidth(image));
            var height = checked((int)CGImageGetHeight(image));
            var stride = checked(width * 4);
            var pixels = Marshal.AllocHGlobal(checked(stride * height));
            var colorSpace = CGColorSpaceCreateDeviceRGB();
            if (colorSpace == 0)
            {
                Marshal.FreeHGlobal(pixels);
                throw new InvalidOperationException("macOS could not create a capture color space.");
            }

            try
            {
                var context = CGBitmapContextCreate(
                    pixels,
                    (nuint)width,
                    (nuint)height,
                    8,
                    (nuint)stride,
                    colorSpace,
                    PremultipliedFirstLittleEndian);
                if (context == 0)
                    throw new InvalidOperationException("macOS could not create a BGRA capture surface.");
                try
                {
                    CGContextTranslateCTM(context, 0, height);
                    CGContextScaleCTM(context, 1, -1);
                    CGContextDrawImage(
                        context,
                        new CGRect { Size = new CGSize { Width = width, Height = height } },
                        image);
                }
                finally
                {
                    CGContextRelease(context);
                }

                using var bgra = Mat.FromPixelData(height, width, MatType.CV_8UC4, pixels, stride);
                var bgr = new Mat();
                Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
                return bgr;
            }
            finally
            {
                CGColorSpaceRelease(colorSpace);
                Marshal.FreeHGlobal(pixels);
            }
        }
        finally
        {
            CGImageRelease(image);
        }
    }

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
    private static extern nint CGWindowListCreateImage(CGRect screenBounds, uint listOption, uint windowId, uint imageOption);
    [DllImport(CoreGraphics)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool CGRectMakeWithDictionaryRepresentation(nint dictionary, out CGRect bounds);
    [DllImport(CoreGraphics)]
    private static extern nuint CGImageGetWidth(nint image);
    [DllImport(CoreGraphics)]
    private static extern nuint CGImageGetHeight(nint image);
    [DllImport(CoreGraphics)]
    private static extern void CGImageRelease(nint image);
    [DllImport(CoreGraphics)]
    private static extern nint CGColorSpaceCreateDeviceRGB();
    [DllImport(CoreGraphics)]
    private static extern void CGColorSpaceRelease(nint colorSpace);
    [DllImport(CoreGraphics)]
    private static extern nint CGBitmapContextCreate(nint data, nuint width, nuint height, nuint bitsPerComponent, nuint bytesPerRow, nint colorSpace, uint bitmapInfo);
    [DllImport(CoreGraphics)]
    private static extern void CGContextTranslateCTM(nint context, double tx, double ty);
    [DllImport(CoreGraphics)]
    private static extern void CGContextScaleCTM(nint context, double sx, double sy);
    [DllImport(CoreGraphics)]
    private static extern void CGContextDrawImage(nint context, CGRect rectangle, nint image);
    [DllImport(CoreGraphics)]
    private static extern void CGContextRelease(nint context);
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
