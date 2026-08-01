using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.EP.WebGpu;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Aviscribe.Core.Ocr;

internal sealed record OcrInferenceOutput(
    float[] Values,
    int TimeSteps,
    int Classes);

internal interface IOcrInferenceSession : IDisposable
{
    string InputName { get; }
    OcrInferenceOutput Run(DenseTensor<float> input);
}

internal interface IOcrInferenceSessionFactory
{
    IOcrInferenceSession Create(string modelPath);
}

internal sealed class OnnxInferenceSessionAdapter : IOcrInferenceSession
{
    private readonly InferenceSession _session;

    public OnnxInferenceSessionAdapter(InferenceSession session)
    {
        _session = session;
        InputName = session.InputMetadata.Keys.First();
    }

    public string InputName { get; }

    public OcrInferenceOutput Run(DenseTensor<float> input)
    {
        using var results = _session.Run(
        [
            NamedOnnxValue.CreateFromTensor(InputName, input)
        ]);
        var output = results.First().AsTensor<float>();
        return new OcrInferenceOutput(
            output.ToArray(),
            output.Dimensions[1],
            output.Dimensions[2]);
    }

    public void Dispose() => _session.Dispose();
}

internal sealed class CpuOnnxInferenceSessionFactory : IOcrInferenceSessionFactory
{
    public IOcrInferenceSession Create(string modelPath)
    {
        return new OnnxInferenceSessionAdapter(new InferenceSession(modelPath));
    }
}

internal sealed class WebGpuOnnxInferenceSessionFactory : IOcrInferenceSessionFactory
{
    private static readonly object RegistrationLock = new();
    private static bool _registered;

    public string DeviceDescription { get; private set; } = "unknown device";

    public IOcrInferenceSession Create(string modelPath)
    {
        var environment = OrtEnv.Instance();
        EnsureRegistered(environment);
        var devices = environment.GetEpDevices()
            .Where(device => device.EpName == WebGpuEp.GetEpName())
            .OrderByDescending(GetPerformanceScore)
            .ThenBy(device => device.HardwareDevice.DeviceId)
            .ToList();
        if (devices.Count == 0)
            throw new InvalidOperationException("No WebGPU device was discovered.");

        var device = devices[0];
        DeviceDescription =
            $"{device.HardwareDevice.Type}; {device.HardwareDevice.Vendor}; " +
            $"device {device.HardwareDevice.DeviceId}";
        using var options = new SessionOptions();
        options.AppendExecutionProvider(
            environment,
            [device],
            new Dictionary<string, string>());
        return new OnnxInferenceSessionAdapter(
            new InferenceSession(modelPath, options));
    }

    private static void EnsureRegistered(OrtEnv environment)
    {
        lock (RegistrationLock)
        {
            if (_registered)
                return;
            environment.RegisterExecutionProviderLibrary(
                "webgpu_ep",
                WebGpuEp.GetLibraryPath());
            _registered = true;
        }
    }

    private static int GetPerformanceScore(OrtEpDevice device)
    {
        var description = string.Join(
            " ",
            device.EpMetadata.Entries
                .Concat(device.HardwareDevice.Metadata.Entries)
                .SelectMany(item => new[] { item.Key, item.Value }));
        if (description.Contains("discrete", StringComparison.OrdinalIgnoreCase) ||
            description.Contains("high-performance", StringComparison.OrdinalIgnoreCase))
        {
            return 2;
        }

        return description.Contains("integrated", StringComparison.OrdinalIgnoreCase)
            ? 0
            : 1;
    }
}
