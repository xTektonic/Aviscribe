using OpenCvSharp;

namespace Aviscribe.Core.Ocr
{
    public static class ImageHash
    {
        public static ulong Compute(Mat mat)
        {
            using var small = mat.Resize(new Size(16, 16));
            using var gray = small.CvtColor(ColorConversionCodes.BGR2GRAY);

            ulong hash = 0;

            for (int y = 0; y < gray.Rows; y++)
            {
                for (int x = 0; x < gray.Cols; x++)
                {
                    hash <<= 1;
                    if (gray.At<byte>(y, x) > 128)
                        hash |= 1;
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
