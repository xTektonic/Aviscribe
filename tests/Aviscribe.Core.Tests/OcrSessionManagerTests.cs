using Aviscribe.Core.Diagnostics;
using Aviscribe.Core.Ocr;
using Microsoft.ML.OnnxRuntime.Tensors;
using System.Text.Json;

namespace Aviscribe.Core.Tests;

public sealed class OcrSessionManagerTests
{
    [Fact]
    public void LegacySettingsDefaultToCpuAndGpuRoundTrips()
    {
        var legacy = JsonSerializer.Deserialize<RunSettings>("{}");
        var gpu = new RunSettings { OcrMode = OcrMode.WebGpu };
        var restored = JsonSerializer.Deserialize<RunSettings>(JsonSerializer.Serialize(gpu));

        Assert.Equal(OcrMode.Cpu, legacy!.OcrMode);
        Assert.Equal(OcrMode.WebGpu, gpu.Clone().OcrMode);
        Assert.Equal(OcrMode.WebGpu, restored!.OcrMode);
    }

    [Fact]
    public void GpuInitializationFailureFallsBackToCpu()
    {
        var cpu = new FakeFactory(new FakeSession());
        var gpu = new FakeFactory(error: new InvalidOperationException("no adapter"));
        using var manager = new OcrSessionManager(
            "model.onnx", OcrMode.WebGpu, cpu, gpu, NullAppDiagnostics.Instance);

        Assert.Equal("CPU", manager.Status.ActiveProvider);
        Assert.Contains("no adapter", manager.Status.FallbackReason);
        Assert.Equal(1, cpu.CreateCount);
    }

    [Fact]
    public void GpuInferenceFailureRecreatesOnCpuAndRetries()
    {
        var gpuSession = new FakeSession(new InvalidOperationException("device lost"));
        var cpuSession = new FakeSession();
        using var manager = new OcrSessionManager(
            "model.onnx",
            OcrMode.WebGpu,
            new FakeFactory(cpuSession),
            new FakeFactory(gpuSession),
            NullAppDiagnostics.Instance);

        var output = manager.Run(new DenseTensor<float>([1, 3, 48, 320]));

        Assert.Single(output.Values);
        Assert.True(gpuSession.Disposed);
        Assert.Equal(1, cpuSession.RunCount);
        Assert.Equal("CPU", manager.Status.ActiveProvider);
        Assert.Contains("device lost", manager.Status.FallbackReason);
    }

    [Fact]
    public void CpuModeNeverCreatesGpuSession()
    {
        var cpu = new FakeFactory(new FakeSession());
        var gpu = new FakeFactory(new FakeSession());
        using var manager = new OcrSessionManager(
            "model.onnx", OcrMode.Cpu, cpu, gpu, NullAppDiagnostics.Instance);

        Assert.Equal(1, cpu.CreateCount);
        Assert.Equal(0, gpu.CreateCount);
        Assert.False(manager.Status.IsFallback);
    }

    private sealed class FakeFactory(
        IOcrInferenceSession? session = null,
        Exception? error = null) : IOcrInferenceSessionFactory
    {
        public int CreateCount { get; private set; }

        public IOcrInferenceSession Create(string modelPath)
        {
            CreateCount++;
            if (error != null)
                throw error;
            return session!;
        }
    }

    private sealed class FakeSession(Exception? error = null) : IOcrInferenceSession
    {
        public string InputName => "input";
        public int RunCount { get; private set; }
        public bool Disposed { get; private set; }

        public OcrInferenceOutput Run(DenseTensor<float> input)
        {
            RunCount++;
            if (error != null)
                throw error;
            return new OcrInferenceOutput([1], 1, 1);
        }

        public void Dispose() => Disposed = true;
    }
}
