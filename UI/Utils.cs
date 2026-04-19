using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using System;
using System.Runtime.InteropServices;
using Aviscribe.Core.Capture;

//using AvaloniaPixelFormat = Avalonia.Platform.PixelFormat;
//using VideoFramePixelFormat = Aviscribe.Core.Capture.PixelFormat;

namespace Aviscribe.UI
{
    internal class Utils
    {

    }

    internal static class Extensions
    {
        public static T GetControl<T>(this Avalonia.Controls.Window window, string name) where T : Avalonia.Controls.Control
        {
            var control = window.FindControl<T>(name);
            if (control == null)
                throw new Exception($"Control '{name}' of type '{typeof(T).Name}' not found.");
            return control;
        }

        //public static WriteableBitmap ToBitmap(this VideoFrame frame)
        //{
        //    AvaloniaPixelFormat format = frame.PixelFormat switch
        //    {
        //        VideoFramePixelFormat.RGB24 => PixelFormats.Rgb24,
        //        VideoFramePixelFormat.BGR24 => PixelFormats.Bgr24,
        //        _ => throw new NotSupportedException($"Unsupported format: {frame.PixelFormat}")
        //    };

        //    int stride = frame.Width * 3;

        //    var bitmap = new WriteableBitmap(
        //        new PixelSize(frame.Width, frame.Height),
        //        new Vector(96, 96),
        //        format,
        //        AlphaFormat.Opaque
        //    );

        //    using (var locked = bitmap.Lock())
        //    {
        //        Marshal.Copy(frame.Data, 0, locked.Address, frame.Data.Length);
        //    }

        //    return bitmap;
        //}
    }
}
