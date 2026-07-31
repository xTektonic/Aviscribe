using System.Runtime.CompilerServices;

// Accord 3.8's .NET Standard video assemblies reference an unsigned
// CoreCompat.System.Drawing 0.0.0.0. This Windows-only shim keeps that assembly
// identity while forwarding the drawing types Accord uses to the supported
// System.Drawing.Common implementation.
[assembly: TypeForwardedTo(typeof(System.Drawing.Bitmap))]
[assembly: TypeForwardedTo(typeof(System.Drawing.CopyPixelOperation))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Graphics))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Image))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Point))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Rectangle))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Size))]
[assembly: TypeForwardedTo(typeof(System.Drawing.SizeF))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Imaging.BitmapData))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Imaging.ColorPalette))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Imaging.ImageLockMode))]
[assembly: TypeForwardedTo(typeof(System.Drawing.Imaging.PixelFormat))]
