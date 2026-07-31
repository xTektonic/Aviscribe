using Aviscribe.Core.Capture;
using OpenCvSharp;

namespace Aviscribe.Core.Ocr;

public static class GameplayFrameNormalizer
{
    public static bool IsAlreadyNormalized(
        Mat rawSource,
        CaptureCropSettings cropSettings)
    {
        ArgumentNullException.ThrowIfNull(rawSource);
        ArgumentNullException.ThrowIfNull(cropSettings);
        if (rawSource.Empty())
            return false;

        var crop = cropSettings.Resolve(
            rawSource.Width,
            rawSource.Height);
        return rawSource.Width == OcrReferenceLayout.Width &&
            rawSource.Height == OcrReferenceLayout.Height &&
            crop.X == 0 &&
            crop.Y == 0 &&
            crop.Width == OcrReferenceLayout.Width &&
            crop.Height == OcrReferenceLayout.Height;
    }

    /// <summary>
    /// Returns a newly owned BGR image containing the selected gameplay crop
    /// at the single OCR reference size of 1920 by 1080.
    /// </summary>
    public static Mat Normalize(
        Mat rawSource,
        CaptureCropSettings cropSettings)
    {
        ArgumentNullException.ThrowIfNull(rawSource);
        ArgumentNullException.ThrowIfNull(cropSettings);
        if (rawSource.Empty())
            throw new ArgumentException(
                "The source frame must not be empty.",
                nameof(rawSource));

        var crop = cropSettings.Resolve(
            rawSource.Width,
            rawSource.Height);
        using var cropped = new Mat(rawSource, crop);
        if (cropped.Width == OcrReferenceLayout.Width &&
            cropped.Height == OcrReferenceLayout.Height)
        {
            return cropped.Clone();
        }

        var normalized = new Mat();
        Cv2.Resize(
            cropped,
            normalized,
            new Size(
                OcrReferenceLayout.Width,
                OcrReferenceLayout.Height),
            interpolation: InterpolationFlags.Linear);
        return normalized;
    }
}
