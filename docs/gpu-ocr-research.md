# GPU OCR research (issue #17)

Research date: August 1, 2026.

## Recommendation

Ship WebGPU as an explicit opt-in while retaining CPU as the default. The
measured hybrid session materially accelerates this fixed-shape OCR model after
a one-time shader warm-up. Session creation and inference exceptions recreate a
CPU-only session and expose the fallback reason in diagnostics.

WebGPU execution remains synchronous, so a native driver hang cannot be
cancelled in-process. If field reports reveal hangs, move GPU OCR into an
isolated helper process with hard startup and inference deadlines.

## Why WebGPU was evaluated

Microsoft's official `Microsoft.ML.OnnxRuntime.EP.WebGpu` 0.1.0 package plugs
into ONNX Runtime 1.24.4 or later, matching Aviscribe's 1.24.4 runtime. It is a
cross-vendor API instead of a CUDA-specific path and ships native assets for
Windows x64/arm64, Linux x64, and macOS arm64. The official usage registers the
plugin with `OrtEnv.RegisterExecutionProviderLibrary`, discovers an
`OrtEpDevice`, and appends that device to session options.

Primary references:

- [ONNX Runtime WebGPU plugin 0.1.0 release](https://github.com/microsoft/onnxruntime/releases/tag/plugin-ep-webgpu%2Fv0.1.0)
- [Microsoft.ML.OnnxRuntime.EP.WebGpu 0.1.0 package](https://www.nuget.org/packages/Microsoft.ML.OnnxRuntime.EP.WebGpu/0.1.0)

Graph capture was not enabled. Aviscribe's existing input remains fixed NCHW
`[1, 3, 48, 320]`.

## Hardware smoke result

The probe ran on the available Windows x64 machine and discovered a WebGPU GPU
device reported by ONNX Runtime as NVIDIA, device ID 11525.

- Plugin registration and device discovery succeeded.
- A WebGPU-only session failed to load because some graph nodes were assigned
  to the CPU provider.
- A hybrid WebGPU + CPU session loaded successfully. ONNX Runtime warned that
  some nodes remained assigned to CPU; this can be normal for shape operations.
- Hybrid session creation took 0.80 seconds.
- CPU warm-up took 594.84 ms; hybrid WebGPU warm-up took 1,901.23 ms.
- Three post-warm-up CPU runs averaged 577.09 ms.
- Three post-warm-up hybrid WebGPU runs averaged 17.07 ms, a 33.82x speedup.

Run the research-only probe after building the classifier tool:

```text
dotnet run --project tools/Aviscribe.Classifier --configuration Release -- ocr-provider-benchmark
```

The command reports each inference separately so a hard outer timeout identifies
the exact stage that failed.

## Packaging impact

The NuGet download is approximately 32.19 MB compressed. Its 0.1.0 native
payloads are:

| Runtime | Native payload |
| --- | ---: |
| Windows x64 | 28,302,288 bytes |
| Windows arm64 | 33,142,696 bytes |
| Linux x64 | 10,153,240 bytes |
| macOS arm64 | 10,073,248 bytes |

Windows includes `onnxruntime_providers_webgpu.dll`, `dxcompiler.dll`, and
`dxil.dll`; Linux and macOS each include the platform WebGPU provider library.
These native assets are selected by runtime identifier in production publishes.
Validated self-contained publish totals on this checkout were 544,001,345 bytes
for Windows x64 and 442,853,429 bytes for Linux x64. The incremental WebGPU
native payload is the runtime-specific amount shown above (plus the 17,720-byte
managed helper assembly).
