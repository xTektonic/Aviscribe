using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public static class ImageHash
    {
        public static ulong Compute(Mat mat)
        {
            if (mat.Empty())
                return 0;

            using var gray = mat.Channels() switch
            {
                1 => mat.Clone(),
                4 => mat.CvtColor(ColorConversionCodes.BGRA2GRAY),
                _ => mat.CvtColor(ColorConversionCodes.BGR2GRAY)
            };
            using var small = gray.Resize(new Size(8, 8), 0, 0, InterpolationFlags.Area);
            var average = Cv2.Mean(small).Val0;

            ulong hash = 0;
            for (int y = 0; y < small.Rows; y++)
            {
                for (int x = 0; x < small.Cols; x++)
                {
                    var bit = y * small.Cols + x;
                    if (small.At<byte>(y, x) > average)
                        hash |= 1UL << bit;
                }
            }

            return hash;
        }

        public static int Hamming(ulong a, ulong b)
        {
            ulong x = a ^ b;
            int count = 0;

            while (x != 0)
            {
                count++;
                x &= x - 1;
            }

            return count;
        }
    }
}
