using Aviscribe.Core.Capture;
using OpenCvSharp;
using System.Runtime.InteropServices;

namespace Aviscribe.Capture;

internal sealed class X11WindowCaptureBackend : IWindowCaptureBackend
{
    private const string X11 = "libX11.so.6";
    private const int ZPixmap = 2;
    private const int LsbFirst = 0;

    public string Name => "X11/XWayland window capture";

    public IReadOnlyList<WindowCaptureTarget> EnumerateTargets()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")) &&
            string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("DISPLAY")))
        {
            return [Unavailable(
                "Native Wayland window capture is not available yet. Start Aviscribe through XWayland, or use a Video Device source. Portal/PipeWire support can be added through the window-capture adapter without changing OCR.")];
        }

        var display = XOpenDisplay(0);
        if (display == 0)
            return [Unavailable("Could not connect to an X11 display. On Wayland, start Aviscribe through XWayland or use a Video Device source.")];

        try
        {
            var candidates = new HashSet<nuint>();
            CollectChildren(display, XDefaultRootWindow(display), candidates, depth: 2);
            var targets = new List<WindowCaptureTarget>();
            foreach (var window in candidates)
            {
                if (XGetGeometry(display, window, out _, out _, out _, out var width, out var height, out _, out _) == 0 ||
                    width < 320 || height < 180)
                    continue;

                var title = GetWindowName(display, window);
                if (string.IsNullOrWhiteSpace(title))
                    continue;

                var windowClass = GetWindowClass(display, window);
                var identity = $"{windowClass}\n{title}";
                targets.Add(new WindowCaptureTarget(
                    VideoDeviceKey.Create("linux", "x11-window", identity, title),
                    title,
                    (nint)window,
                    checked((int)width),
                    checked((int)height)));
            }

            var result = targets
                .GroupBy(target => target.Id, StringComparer.Ordinal)
                .Select(group => group.First())
                .OrderBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return result.Length > 0
                ? result
                : [Unavailable("No capturable X11/XWayland windows were found. Native Wayland windows are not currently visible to Aviscribe.")];
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    public Mat Capture(WindowCaptureTarget target)
    {
        var display = XOpenDisplay(0);
        if (display == 0)
            throw new InvalidOperationException("The X11 display is no longer available.");

        try
        {
            var window = (nuint)target.NativeHandle;
            if (XGetGeometry(display, window, out _, out _, out _, out var width, out var height, out _, out _) == 0 || width == 0 || height == 0)
                throw new InvalidOperationException("The selected window was closed or minimized.");

            var imagePointer = XGetImage(display, window, 0, 0, width, height, nuint.MaxValue, ZPixmap);
            if (imagePointer == 0)
                throw new InvalidOperationException("X11 could not read the selected window. Restore it and try again.");

            try
            {
                var image = Marshal.PtrToStructure<XImage>(imagePointer);
                if (image.ByteOrder != LsbFirst ||
                    image.RedMask != 0x00ff0000 ||
                    image.GreenMask != 0x0000ff00 ||
                    image.BlueMask != 0x000000ff)
                {
                    throw new InvalidOperationException(
                        "X11 returned an unsupported pixel channel layout.");
                }

                if (image.BitsPerPixel == 32)
                {
                    using var bgra = Mat.FromPixelData(image.Height, image.Width, MatType.CV_8UC4, image.Data, image.BytesPerLine);
                    var bgr = new Mat();
                    Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
                    return bgr;
                }

                if (image.BitsPerPixel == 24)
                {
                    using var bgr = Mat.FromPixelData(image.Height, image.Width, MatType.CV_8UC3, image.Data, image.BytesPerLine);
                    return bgr.Clone();
                }

                throw new InvalidOperationException($"X11 returned an unsupported {image.BitsPerPixel}-bit pixel format.");
            }
            finally
            {
                XDestroyImage(imagePointer);
            }
        }
        finally
        {
            XCloseDisplay(display);
        }
    }

    private static WindowCaptureTarget Unavailable(string reason) =>
        new("window:linux:unavailable", reason, 0, 0, 0, false, reason);

    private static void CollectChildren(nint display, nuint parent, HashSet<nuint> result, int depth)
    {
        if (depth <= 0 || XQueryTree(display, parent, out _, out _, out var children, out var count) == 0)
            return;
        try
        {
            for (var index = 0u; index < count; index++)
            {
                var child = (nuint)Marshal.ReadIntPtr(children, checked((int)index * IntPtr.Size));
                if (child == 0 || !result.Add(child))
                    continue;
                CollectChildren(display, child, result, depth - 1);
            }
        }
        finally
        {
            if (children != 0)
                XFree(children);
        }
    }

    private static string GetWindowName(nint display, nuint window)
    {
        if (XFetchName(display, window, out var name) == 0 || name == 0)
            return string.Empty;
        try { return Marshal.PtrToStringUTF8(name)?.Trim() ?? string.Empty; }
        finally { XFree(name); }
    }

    private static string GetWindowClass(nint display, nuint window)
    {
        if (XGetClassHint(display, window, out var hint) == 0)
            return string.Empty;
        try
        {
            var resourceName = hint.ResourceName == 0 ? "" : Marshal.PtrToStringUTF8(hint.ResourceName);
            var resourceClass = hint.ResourceClass == 0 ? "" : Marshal.PtrToStringUTF8(hint.ResourceClass);
            return $"{resourceName}/{resourceClass}";
        }
        finally
        {
            if (hint.ResourceName != 0) XFree(hint.ResourceName);
            if (hint.ResourceClass != 0) XFree(hint.ResourceClass);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct XClassHint { public nint ResourceName; public nint ResourceClass; }

    [StructLayout(LayoutKind.Sequential)]
    private struct XImage
    {
        public int Width;
        public int Height;
        public int XOffset;
        public int Format;
        public nint Data;
        public int ByteOrder;
        public int BitmapUnit;
        public int BitmapBitOrder;
        public int BitmapPad;
        public int Depth;
        public int BytesPerLine;
        public int BitsPerPixel;
        public nuint RedMask;
        public nuint GreenMask;
        public nuint BlueMask;
        public nint ObData;
        public nint Functions;
    }

    [DllImport(X11)]
    private static extern nint XOpenDisplay(nint displayName);
    [DllImport(X11)]
    private static extern int XCloseDisplay(nint display);
    [DllImport(X11)]
    private static extern nuint XDefaultRootWindow(nint display);
    [DllImport(X11)]
    private static extern int XQueryTree(nint display, nuint window, out nuint root, out nuint parent, out nint children, out uint childCount);
    [DllImport(X11)]
    private static extern int XFetchName(nint display, nuint window, out nint name);
    [DllImport(X11)]
    private static extern int XGetClassHint(nint display, nuint window, out XClassHint hint);
    [DllImport(X11)]
    private static extern int XGetGeometry(nint display, nuint drawable, out nuint root, out int x, out int y, out uint width, out uint height, out uint borderWidth, out uint depth);
    [DllImport(X11)]
    private static extern nint XGetImage(nint display, nuint drawable, int x, int y, uint width, uint height, nuint planeMask, int format);
    [DllImport(X11)]
    private static extern int XDestroyImage(nint image);
    [DllImport(X11)]
    private static extern int XFree(nint data);
}
