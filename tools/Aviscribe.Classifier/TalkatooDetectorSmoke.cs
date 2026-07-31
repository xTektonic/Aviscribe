using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class TalkatooDetectorSmoke
    {
        public static void Run()
        {
            AcceptsShortThreeCharacterBand();
            RejectsYellowSceneryWithoutCrescent();
            RejectsCrescentWithoutAdjacentText();
            AcceptsLeftClippedMarker();
            RejectsWhiteOverlay();
            Console.WriteLine("Talkatoo detector smoke passed.");
        }

        private static void AcceptsShortThreeCharacterBand()
        {
            using var image = CreateCrop();
            DrawFullMarker(image, x: 10);
            DrawThreeCharacterBand(image, x: 90);
            AssertDetected(image, "short three-character yellow band");
        }

        private static void RejectsYellowSceneryWithoutCrescent()
        {
            using var image = CreateCrop();
            Cv2.Rectangle(
                image,
                new Rect(80, 4, 360, 38),
                new Scalar(20, 220, 240),
                thickness: -1);
            AssertRejected(image, "yellow scenery without a crescent");
        }

        private static void RejectsCrescentWithoutAdjacentText()
        {
            using var image = CreateCrop();
            DrawFullMarker(image, x: 10);
            DrawThreeCharacterBand(image, x: 150);
            AssertRejected(image, "crescent-like component without adjacent text");
        }

        private static void AcceptsLeftClippedMarker()
        {
            using var image = CreateCrop();
            DrawLeftClippedMarker(image);
            DrawThreeCharacterBand(image, x: 60);
            AssertDetected(image, "left-clipped Talkatoo marker");
        }

        private static void RejectsWhiteOverlay()
        {
            using var image = CreateCrop();
            DrawFullMarker(image, x: 10);

            for (var character = 0; character < 8; character++)
            {
                Cv2.Rectangle(
                    image,
                    new Rect(90 + character * 18, 9, 12, 26),
                    Scalar.White,
                    thickness: -1);
            }

            AssertRejected(image, "white collection/result overlay");
        }

        private static Mat CreateCrop()
        {
            return new Mat(new Size(649, 48), MatType.CV_8UC3, Scalar.Black);
        }

        private static void DrawFullMarker(Mat image, int x)
        {
            const int width = 52;
            const int height = 42;
            const int thickness = 7;

            for (var y = 0; y < height; y++)
            {
                for (var localX = 0; localX < width; localX++)
                {
                    if (localX < thickness ||
                        localX >= width - thickness ||
                        y < thickness ||
                        y >= height - thickness)
                    {
                        image.Set(y, x + localX, new Vec3b(240, 240, 240));
                    }
                }
            }
        }

        private static void DrawLeftClippedMarker(Mat image)
        {
            const int width = 18;
            const int height = 40;
            const int thickness = 3;

            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    if (x < thickness ||
                        x >= width - thickness ||
                        y < thickness ||
                        y >= height - thickness)
                    {
                        image.Set(y, x, new Vec3b(240, 240, 240));
                    }
                }
            }
        }

        private static void DrawThreeCharacterBand(Mat image, int x)
        {
            for (var character = 0; character < 3; character++)
            {
                Cv2.Rectangle(
                    image,
                    new Rect(x + character * 16, 8, 12, 24),
                    new Scalar(20, 220, 240),
                    thickness: -1);
            }
        }

        private static void AssertDetected(Mat image, string scenario)
        {
            if (!TextDetection.HasTalkatooText(image))
                throw new InvalidOperationException(
                    $"Talkatoo detector rejected {scenario}.");
        }

        private static void AssertRejected(Mat image, string scenario)
        {
            if (TextDetection.HasTalkatooText(image))
                throw new InvalidOperationException(
                    $"Talkatoo detector accepted {scenario}.");
        }
    }
}
