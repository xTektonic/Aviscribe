using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Text;
using Aviscribe.Core.Diagnostics;

namespace Aviscribe.Core.Ocr
{
    public class OnnxOcrService : IOcrService, IDisposable
    {
        private readonly IOcrInferenceSession _session;
        private readonly string[] _charset;

        private const int TargetHeight = 48;
        private const int MaxWidth = 320; // safe cap

        public OnnxOcrService(string modelPath, string dictPath)
            : this(modelPath, dictPath, OcrMode.Cpu, NullAppDiagnostics.Instance)
        {
        }

        public OnnxOcrService(
            string modelPath,
            string dictPath,
            OcrMode requestedMode,
            IAppDiagnostics? diagnostics = null)
            : this(
                dictPath,
                new OcrSessionManager(
                    modelPath,
                    requestedMode,
                    new CpuOnnxInferenceSessionFactory(),
                    new WebGpuOnnxInferenceSessionFactory(),
                    diagnostics ?? NullAppDiagnostics.Instance))
        {
        }

        internal OnnxOcrService(
            string modelPath,
            string dictPath,
            IOcrInferenceSessionFactory sessionFactory)
            : this(dictPath, sessionFactory.Create(modelPath))
        {
        }

        private OnnxOcrService(
            string dictPath,
            IOcrInferenceSession session)
        {
            _session = session;
            _charset = System.IO.File.ReadAllLines(dictPath);
        }

        public OcrRuntimeStatus RuntimeStatus =>
            (_session as OcrSessionManager)?.Status ??
            new OcrRuntimeStatus(OcrMode.Cpu, "CPU", "CPU");

        public string ReadText(Mat image)
        {
            if (image.Empty())
                return string.Empty;

            //DateTime start = DateTime.UtcNow;
            using var inputMat = Preprocess(image);
            //Console.WriteLine($"ran preprocess in {(DateTime.UtcNow - start)}");

            //start = DateTime.UtcNow;
            var tensor = ToTensor(inputMat);
            //Console.WriteLine($"ran tensor in {(DateTime.UtcNow - start)}");

            //start = DateTime.UtcNow;
            var output = _session.Run(tensor);
            //Console.WriteLine($"ran session in {(DateTime.UtcNow - start)}");

            //start = DateTime.UtcNow;
            var text = Decode(output);
            //Console.WriteLine($"ran decode in {(DateTime.UtcNow - start)}");

            return text.Replace("\n", "").Trim();
        }

        // ----------------------------
        // PREPROCESS (PP-OCRv5 CORRECT)
        // ----------------------------
        private Mat Preprocess(Mat src)
        {
            int newWidth = (int)(src.Width * (TargetHeight / (float)src.Height));
            newWidth = Math.Min(newWidth, MaxWidth);

            Mat resized = new Mat();
            Cv2.Resize(src, resized, new Size(newWidth, TargetHeight));

            // pad to fixed width (important for stable tensor shape)
            Mat padded = new Mat(new Size(MaxWidth, TargetHeight), MatType.CV_8UC3, Scalar.Black);
            resized.CopyTo(padded[new Rect(0, 0, resized.Width, resized.Height)]);

            // ensure 3 channels
            if (padded.Channels() == 1)
            {
                Cv2.CvtColor(padded, padded, ColorConversionCodes.GRAY2BGR);
            }

            // normalize to [0,1]
            padded.ConvertTo(padded, MatType.CV_32FC3, 1.0 / 255.0);

            return padded;
        }

        // ----------------------------
        // TENSOR CONVERSION
        // ----------------------------
        private DenseTensor<float> ToTensor(Mat img)
        {
            var tensor = new DenseTensor<float>(new[] { 1, 3, TargetHeight, MaxWidth });

            for (int y = 0; y < TargetHeight; y++)
            {
                for (int x = 0; x < MaxWidth; x++)
                {
                    Vec3f pixel = img.At<Vec3f>(y, x);

                    tensor[0, 0, y, x] = pixel.Item0;
                    tensor[0, 1, y, x] = pixel.Item1;
                    tensor[0, 2, y, x] = pixel.Item2;
                }
            }

            return tensor;
        }

        // ----------------------------
        // CTC DECODER (FIXED)
        // ----------------------------
        private string Decode(OcrInferenceOutput output)
        {
            int timeSteps = output.TimeSteps;
            int classes = output.Classes;

            int lastIndex = -1;
            var sb = new StringBuilder();

            for (int t = 0; t < timeSteps; t++)
            {
                int best = 0;
                float bestScore = float.MinValue;

                for (int c = 0; c < classes; c++)
                {
                    float val = output.Values[t * classes + c];
                    if (val > bestScore)
                    {
                        bestScore = val;
                        best = c;
                    }
                }

                // skip blank (0) + repeats
                if (best != lastIndex && best > 0 && best - 1 < _charset.Length)
                {
                    sb.Append(_charset[best - 1]); // IMPORTANT: -1 offset
                }

                lastIndex = best;
            }

            return sb.ToString();
        }

        public void Dispose()
        {
            _session.Dispose();
        }
    }
}
