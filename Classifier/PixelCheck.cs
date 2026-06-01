using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal readonly record struct PixelCheck(int X, int Y, byte R, byte G, byte B);

    internal static class PixelChecker
    {
        public static unsafe bool AllPixelsMatchFast(Mat mat, IReadOnlyList<PixelCheck> checks)
        {
            if (mat.Empty() || mat.Type() != MatType.CV_8UC3)
                return false;

            foreach (var check in checks)
            {
                if (check.X < 0 || check.Y < 0 || check.X >= mat.Cols || check.Y >= mat.Rows)
                    return false;

                var pixel = *((Vec3b*)mat.Ptr(check.Y, check.X));

                if (pixel.Item2 != check.R || pixel.Item1 != check.G || pixel.Item0 != check.B)
                    return false;
            }

            return true;
        }
    }
}
