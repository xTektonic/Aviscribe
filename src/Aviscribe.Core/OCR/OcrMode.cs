namespace Aviscribe.Core.Ocr;

public enum OcrMode
{
    Cpu,
    WebGpu
}

public sealed record OcrRuntimeStatus(
    OcrMode RequestedMode,
    string ActiveProvider,
    string ActiveDevice,
    string? FallbackReason = null)
{
    public bool IsFallback => !string.IsNullOrWhiteSpace(FallbackReason);
}
