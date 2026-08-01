using Aviscribe.Core.Diagnostics;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Aviscribe.Core.Ocr;

internal sealed class OcrSessionManager : IOcrInferenceSession
{
    private readonly object _sync = new();
    private readonly string _modelPath;
    private readonly IOcrInferenceSessionFactory _cpuFactory;
    private readonly IOcrInferenceSessionFactory _gpuFactory;
    private readonly IAppDiagnostics _diagnostics;
    private IOcrInferenceSession _session;
    private OcrRuntimeStatus _status;
    private bool _disposed;

    public OcrSessionManager(
        string modelPath,
        OcrMode requestedMode,
        IOcrInferenceSessionFactory cpuFactory,
        IOcrInferenceSessionFactory gpuFactory,
        IAppDiagnostics diagnostics)
    {
        _modelPath = modelPath;
        _cpuFactory = cpuFactory;
        _gpuFactory = gpuFactory;
        _diagnostics = diagnostics;
        (_session, _status) = CreateSession(requestedMode);
    }

    public string InputName => _session.InputName;

    public OcrRuntimeStatus Status
    {
        get
        {
            lock (_sync)
                return _status;
        }
    }

    public OcrInferenceOutput Run(DenseTensor<float> input)
    {
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            try
            {
                return _session.Run(input);
            }
            catch (Exception ex) when (_status.RequestedMode == OcrMode.WebGpu &&
                                       _status.ActiveProvider.StartsWith("WebGPU", StringComparison.Ordinal))
            {
                var reason = Reason("WebGPU inference failed", ex);
                _diagnostics.Error($"{reason} Falling back to CPU OCR.", ex);
                try
                {
                    _session.Dispose();
                }
                catch (Exception disposeException)
                {
                    _diagnostics.Error(
                        "Could not dispose the failed WebGPU OCR session.",
                        disposeException);
                }
                _session = _cpuFactory.Create(_modelPath);
                _status = new OcrRuntimeStatus(OcrMode.WebGpu, "CPU", "CPU", reason);
                return _session.Run(input);
            }
        }
    }

    private (IOcrInferenceSession, OcrRuntimeStatus) CreateSession(OcrMode mode)
    {
        if (mode == OcrMode.WebGpu)
        {
            try
            {
                var session = _gpuFactory.Create(_modelPath);
                var device = (_gpuFactory as WebGpuOnnxInferenceSessionFactory)?
                    .DeviceDescription ?? "WebGPU device";
                _diagnostics.Information(
                    $"OCR requested WebGPU; active provider WebGPU + CPU ({device}). " +
                    "Unsupported graph nodes remain on CPU.");
                return (session, new OcrRuntimeStatus(mode, "WebGPU + CPU", device));
            }
            catch (Exception ex)
            {
                var reason = Reason("WebGPU initialization failed", ex);
                _diagnostics.Error($"{reason} Falling back to CPU OCR.", ex);
                return (
                    _cpuFactory.Create(_modelPath),
                    new OcrRuntimeStatus(mode, "CPU", "CPU", reason));
            }
        }

        _diagnostics.Information("OCR requested CPU; active provider CPU.");
        return (_cpuFactory.Create(_modelPath), new OcrRuntimeStatus(mode, "CPU", "CPU"));
    }

    private static string Reason(string prefix, Exception exception)
    {
        var message = exception.Message.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return string.IsNullOrWhiteSpace(message)
            ? $"{prefix} ({exception.GetType().Name})."
            : $"{prefix}: {message}";
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            _disposed = true;
            _session.Dispose();
        }
    }
}
