# Aviscribe integration validation

`Aviscribe.Validation` validates Aviscribe's SMOVision integration. Legacy
detector training and recognition experiments have been removed; recommendations
for upstream replacements are in [the ledger](../../docs/smovision-integration-ledger.md).

Prepare the pinned feed and build the solution first (see the root README).

```text
dotnet run --project tools/Aviscribe.Validation -c Release --no-build -- state-smoke
dotnet run --project tools/Aviscribe.Validation -c Release --no-build -- smovision-smoke
dotnet run --project tools/Aviscribe.Validation -c Release --no-build -- replay <video> <initial-kingdom> <output.json> [expected-facts.json] [--cpu] [--adaptive]
```

`state-smoke` checks existing application rules. `smovision-smoke` checks the
836-moon catalog, packaged OCR hashes, real CPU inference for English and both Chinese variants, and provider
construction without accessing a camera or window.

`replay` feeds decoded frames into SMOVision in Offline mode and uses the same recognition/eligibility
adapter as live capture. FFmpeg and FFprobe must be on PATH. It exports run facts,
ambiguous reviews, coverage and detector statistics. Initial kingdom names use
Aviscribe's existing names (for example Metro or Bowsers). Automatic kingdom
switching is enabled; post-game eligibility is enabled for developer replay.
`--adaptive` changes Talkatoo only, matching the desktop setting. `--cpu` bypasses
GPU discovery. Without it, SMOVision prefers WebGPU with CPU fallback.

The optional expected file contains the JSON array from an earlier reviewed
`Facts` result. Comparison is by kingdom/moon identity and hinted/collected/manual
classification, independent of occurrence GUIDs and detector timing. A mismatch,
incomplete execution, or detector fault returns a nonzero exit code. Review
results still require human validation; a completed replay is not an accuracy claim.

Video input is a developer tool only. No new capture input or UI feature is added
to Aviscribe. Footage, generated clips, and result files should stay outside Git.

SMOVision beta.1 removed the Media package. `FfmpegVideoSource.cs` is vendored
from its [tagged sample](https://github.com/xTektonic/SMOVision/blob/ea286dd22c0ba7b70e2dc1d6d42ca76ad7467193/samples/SMOVision.Sample.Media/FfmpegVideoSource.cs),
with only its namespace changed and a provenance header added. It uses
AGPL-3.0-or-later; the complete text is in `SMOVision-LICENSE.txt`. No alpha
assembly or sibling source-project reference is used. FFmpeg is caller installed.
