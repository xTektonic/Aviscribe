using Microsoft.Win32.SafeHandles;
using OpenCvSharp;
using System.Runtime.InteropServices;
using System.Text;

namespace Aviscribe.Capture;

internal sealed class MacScreenCaptureSession : IWindowCaptureSession
{
    private const string Library = "libAviscribeScreenCapture.dylib";
    private readonly ScreenHandle _handle;

    public MacScreenCaptureSession(uint windowId)
    {
        _handle = Open(windowId);
        if (_handle.IsInvalid)
        {
            _handle.Dispose();
            throw new InvalidOperationException("Could not create a ScreenCaptureKit session.");
        }
    }

    public Mat Capture()
    {
        var error = new byte[2048];
        var result = Read(_handle, out var pixels, out var width, out var height,
            out var stride, error, error.Length);
        try
        {
            if (result == 0)
            {
                var end = Array.IndexOf(error, (byte)0);
                throw new InvalidOperationException(
                    Encoding.UTF8.GetString(error, 0, end < 0 ? error.Length : end));
            }
            if (pixels == 0 || width <= 0 || height <= 0 || width > 4096 || height > 4096 ||
                stride < checked(width * 4) || (long)stride * height > 128 * 1024 * 1024)
                throw new InvalidDataException("ScreenCaptureKit returned an invalid frame layout.");

            using var bgra = Mat.FromPixelData(height, width, MatType.CV_8UC4, pixels, stride);
            var bgr = new Mat();
            try
            {
                Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);
                return bgr;
            }
            catch
            {
                bgr.Dispose();
                throw;
            }
        }
        finally
        {
            if (pixels != 0) Free(pixels);
        }
    }

    public void Dispose() => _handle.Dispose();

    private sealed class ScreenHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public ScreenHandle() : base(ownsHandle: true) { }
        protected override bool ReleaseHandle()
        {
            MacScreenCaptureSession.Close(handle);
            return true;
        }
    }

    [DllImport(Library, EntryPoint = "aviscribe_screen_open", CallingConvention = CallingConvention.Cdecl)]
    private static extern ScreenHandle Open(uint windowId);
    [DllImport(Library, EntryPoint = "aviscribe_screen_read", CallingConvention = CallingConvention.Cdecl)]
    private static extern int Read(ScreenHandle handle, out nint pixels, out int width,
        out int height, out int stride, [Out] byte[] error, int errorCapacity);
    [DllImport(Library, EntryPoint = "aviscribe_screen_close", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Close(nint handle);
    [DllImport(Library, EntryPoint = "aviscribe_screen_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Free(nint pixels);
}
