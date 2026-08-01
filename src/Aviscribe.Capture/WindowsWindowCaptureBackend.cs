using Aviscribe.Core.Capture;
using OpenCvSharp;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Aviscribe.Capture;

internal sealed class WindowsWindowCaptureBackend : IWindowCaptureBackend
{
    private const uint DibRgbColors = 0;
    private const uint Srccopy = 0x00CC0020;
    private const uint CaptureBlt = 0x40000000;
    private const uint PwRenderFullContent = 2;

    public string Name => "Win32 window capture";

    public IReadOnlyList<WindowCaptureTarget> EnumerateTargets()
    {
        var targets = new List<WindowCaptureTarget>();
        EnumWindows((handle, _) =>
        {
            if (!IsWindowVisible(handle) || GetWindowTextLengthW(handle) == 0 || !GetWindowRect(handle, out var bounds))
                return true;

            var width = bounds.Right - bounds.Left;
            var height = bounds.Bottom - bounds.Top;
            if (width < 320 || height < 180)
                return true;

            var title = GetText(handle);
            var className = GetClass(handle);
            GetWindowThreadProcessId(handle, out var processId);
            var owner = GetProcessIdentity(processId);
            var identity = $"{owner}\n{className}\n{title}";
            targets.Add(new WindowCaptureTarget(
                VideoDeviceKey.Create("windows", "window", identity, title),
                title,
                handle,
                width,
                height));
            return true;
        }, 0);

        return targets
            .GroupBy(target => target.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(target => target.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public Mat Capture(WindowCaptureTarget target)
    {
        var handle = target.NativeHandle;
        if (handle == 0 || !IsWindow(handle) || !GetWindowRect(handle, out var bounds))
            throw new InvalidOperationException("The selected window was closed.");

        var width = bounds.Right - bounds.Left;
        var height = bounds.Bottom - bounds.Top;
        if (width <= 0 || height <= 0)
            throw new InvalidOperationException("The selected window has no capturable area.");

        var windowDc = GetWindowDC(handle);
        if (windowDc == 0)
            throw new InvalidOperationException("Windows did not provide a device context for the selected window.");

        var memoryDc = CreateCompatibleDC(windowDc);
        if (memoryDc == 0)
        {
            ReleaseDC(handle, windowDc);
            throw new InvalidOperationException("Windows could not allocate a compatible capture context.");
        }
        nint bitmap = 0;
        nint previous = 0;
        try
        {
            var bitmapInfo = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = width,
                    Height = -height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0
                }
            };
            bitmap = CreateDIBSection(windowDc, ref bitmapInfo, DibRgbColors, out var pixels, 0, 0);
            if (bitmap == 0 || pixels == 0)
                throw new InvalidOperationException("Windows could not allocate a capture surface.");

            previous = SelectObject(memoryDc, bitmap);
            var rendered = PrintWindow(handle, memoryDc, PwRenderFullContent);
            if (!rendered)
                rendered = BitBlt(memoryDc, 0, 0, width, height, windowDc, 0, 0, Srccopy | CaptureBlt);
            if (!rendered)
                throw new InvalidOperationException("The window refused capture. Restore it or disable protected-content rendering and try again.");

            using var bgra = Mat.FromPixelData(height, width, MatType.CV_8UC4, pixels, width * 4);
            var bgr = new Mat();
            Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
            return bgr;
        }
        finally
        {
            if (previous != 0)
                SelectObject(memoryDc, previous);
            if (bitmap != 0)
                DeleteObject(bitmap);
            if (memoryDc != 0)
                DeleteDC(memoryDc);
            ReleaseDC(handle, windowDc);
        }
    }

    private static string GetText(nint handle)
    {
        var buffer = new StringBuilder(GetWindowTextLengthW(handle) + 1);
        GetWindowTextW(handle, buffer, buffer.Capacity);
        return buffer.ToString().Trim();
    }

    private static string GetClass(nint handle)
    {
        var buffer = new StringBuilder(256);
        GetClassNameW(handle, buffer, buffer.Capacity);
        return buffer.ToString();
    }

    private static string GetProcessIdentity(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.MainModule?.FileName ?? process.ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left; public int Top; public int Right; public int Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ClrUsed;
        public uint ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo { public BitmapInfoHeader Header; public uint Colors; }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")]
    private static extern bool IsWindow(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLengthW(nint handle);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextW(nint handle, StringBuilder text, int count);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetClassNameW(nint handle, StringBuilder text, int count);
    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(nint handle, out Rect bounds);
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll")]
    private static extern nint GetWindowDC(nint handle);
    [DllImport("user32.dll")]
    private static extern int ReleaseDC(nint handle, nint dc);
    [DllImport("user32.dll")]
    private static extern bool PrintWindow(nint handle, nint dc, uint flags);
    [DllImport("gdi32.dll")]
    private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")]
    private static extern nint CreateDIBSection(nint dc, ref BitmapInfo info, uint usage, out nint bits, nint section, uint offset);
    [DllImport("gdi32.dll")]
    private static extern nint SelectObject(nint dc, nint value);
    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(nint value);
    [DllImport("gdi32.dll")]
    private static extern bool BitBlt(nint destination, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint operation);
}
