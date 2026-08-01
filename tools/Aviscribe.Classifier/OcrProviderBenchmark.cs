using Aviscribe.Core;
using Aviscribe.Core.Ocr;
using OpenCvSharp;
using System.Diagnostics;

namespace Aviscribe.Classifier;

internal static class OcrProviderBenchmark
{
    private const int MeasuredRuns = 3;

    public static void Run()
    {
        using var image = new Mat(
            new Size(320, 48),
            MatType.CV_8UC3,
            Scalar.Black);
        using var cpu = new OnnxOcrService(
            AppPaths.OcrModelPath,
            AppPaths.CharsetPath);

        var gpuFactory = new WebGpuOnnxInferenceSessionFactory();
        var createTimer = Stopwatch.StartNew();
        using var gpu = new OnnxOcrService(
            AppPaths.OcrModelPath,
            AppPaths.CharsetPath,
            gpuFactory);
        createTimer.Stop();
        Console.WriteLine(
            $"WebGPU hybrid session loaded in {createTimer.Elapsed.TotalSeconds:F2}s: " +
            gpuFactory.DeviceDescription);

        Console.WriteLine("CPU warm-up inference starting...");
        Console.WriteLine($"CPU warm-up: {MeasureOnce(cpu, image):F2} ms");
        Console.WriteLine("WebGPU hybrid warm-up inference starting...");
        Console.WriteLine($"WebGPU hybrid warm-up: {MeasureOnce(gpu, image):F2} ms");

        var cpuMean = MeasureRepeated("CPU", cpu, image);
        var gpuMean = MeasureRepeated("WebGPU hybrid", gpu, image);
        Console.WriteLine($"CPU mean ({MeasuredRuns} runs): {cpuMean:F2} ms");
        Console.WriteLine($"WebGPU hybrid mean ({MeasuredRuns} runs): {gpuMean:F2} ms");
        Console.WriteLine($"CPU/WebGPU ratio: {cpuMean / gpuMean:F2}x");
    }

    private static double MeasureRepeated(
        string label,
        OnnxOcrService service,
        Mat image)
    {
        var total = 0d;
        for (var index = 0; index < MeasuredRuns; index++)
        {
            var elapsed = MeasureOnce(service, image);
            Console.WriteLine($"{label} run {index + 1}/{MeasuredRuns}: {elapsed:F2} ms");
            total += elapsed;
        }

        return total / MeasuredRuns;
    }

    private static double MeasureOnce(OnnxOcrService service, Mat image)
    {
        var stopwatch = Stopwatch.StartNew();
        service.ReadText(image);
        stopwatch.Stop();
        return stopwatch.Elapsed.TotalMilliseconds;
    }

}
