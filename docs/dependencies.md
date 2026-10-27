# Integration dependencies

This integration uses the released SMOVision 0.1.0-beta.2 packages, identified by
`tools/smovision-packages.sha256`. Capture, recognition, catalog and OCR assets are
owned by those packages; Aviscribe no longer ships its own copies of the engine
or recognition data. Pending-moon previews come from the optional `SMOVision.Assets` package through
`MoonImages.OpenRead`; Aviscribe owns the decoded bitmap cache and its disposal.
The 836 duplicate PNGs are no longer stored in this repository.

Aviscribe's original MIT notice remains in the repository root.
SMOVision's six beta packages declare **AGPL-3.0-or-later** and include their
license and notices. The developer replay helper is copied from the exact beta
tag with its license retained in `tools/Aviscribe.Validation/SMOVision-LICENSE.txt`.
See the release's [licensing documentation](https://github.com/xTektonic/SMOVision/blob/ea286dd22c0ba7b70e2dc1d6d42ca76ad7467193/docs/licensing.md)
for distribution and corresponding-source requirements. This update does not
publish an Aviscribe release.

The supplied SMOVision documentation attributes adapted capture/recognition code,
catalog and original regional icons to Aviscribe and includes its MIT notice.
SMOVision's native packages retain their own notices. Preserve those notices when
redistributing the application's dependencies.

| Dependency | Usage | License recorded in supplied documentation |
| --- | --- | --- |
| OpenCvSharp4 and platform runtimes 4.13.0.20260627 | SMOVision imaging and app preview/crop rendering | Apache 2.0 |
| ONNX Runtime 1.24.4 and WebGPU provider 0.1.0 | SMOVision OCR | ONNX Runtime MIT; retain provider package notices |
| FlashCap / FlashCap.Core 1.11.10 | Capture devices, exact supplied fork archives | Apache 2.0 |
| PipeWire.NET 0.2.1-alpha-aviscribe.1 | Wayland capture, exact supplied fork archive | MIT |
| Tmds.DBus 0.94.2 | Desktop capture portal | MIT |
| FFmpeg / FFprobe | Optional developer replay, caller installed | Depends on the caller's build; not bundled by Aviscribe |

The beta OCR package replaces the former Aviscribe model with PaddlePaddle's
**PP-OCRv6_small_rec** official ONNX export and matching dictionary (Apache 2.0),
from revision `b8f84f0b80c529de40b4fbb3544b84fa7233a513`. The model provenance
manifest and PaddleOCR notice are included in the package. Aviscribe's smoke check
verifies both deployed asset hashes against `BundledOcrAssets` and performs real
CPU inference. Recognition differences from the alpha model follow SMOVision;
no legacy OCR fallback is retained. Game text and imagery remain associated with
Super Mario Odyssey and their rights holders.

Consult the supplied SMOVision `docs/third-party-notices.md` and individual package
notices when preparing a distributable release. The preparation script downloads and verifies the pinned release bundle, or accepts
an extracted feed. Hosted download access and cross-platform release validation
remain tracked in the integration ledger.
