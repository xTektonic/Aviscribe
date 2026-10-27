# SMOVision integration alpha ledger

Aviscribe branch: `smovision-integration-alpha`, based on `release/1.1.1` (`ab6d0cc`).
Dependency: [SMOVision `0.1.0-beta.2`](https://github.com/xTektonic/SMOVision/releases/tag/v0.1.0-beta.2),
source commit `495085482552e1fc1b847397c070009220adda90`. The nine archive hashes
in `tools/smovision-packages.sha256` identify the released inputs; the preparation
script also pins the complete release ZIP hash. Feed and cache paths include the
beta version. No source checkout is rebuilt or substituted.
No SMOVision repository files were changed by this integration.

## Ownership and compatibility

- SMOVision owns native capture, frame scheduling, presence/confirmation, OCR,
  moon-name matching, kingdom recognition, and the catalog. There is no legacy
  recognition fallback. Only Talkatoo, collection, and optional kingdom detectors
  are enabled. Coins, HUD stacks, checkpoint, collection-date, and hint-art-viewer
  features remain disabled.
- Aviscribe owns run categories, eligibility, route order, manual corrections,
  pending/counting projection, online protocol, saved settings, review UI, preview,
  crop selection, and text output. Hint-art *moon collection* rules remain intact;
  the separate SMOVision artwork-viewer detector is not enabled.
- App capture DTOs now live inside Aviscribe.Core. Their namespace and enum values
  remain compatible with existing saves. SMOVision capture kinds are mapped by
  meaning, not by integer cast. Crop settings retain the legacy scale/16:9 behavior.
- All 836 catalog rows and all 13 language columns match the release catalog.
  The legacy online catalog hash remains
  `E14A68157DC02D844FCA88C79507ACF9AB78AEEEA32CE4CB08EBBB28BDEF5DAF`.
- Accepted occurrences use a reliable analysis sink with acknowledgement before
  publication continues. Application work is dispatched outside the library's
  callback, and UI rendering is posted separately. Old context is invalidated on
  source/crop/kingdom/settings/run changes. The live input and snapshots are bounded
  leases; native cleanup is awaited asynchronously.
- Recognition thresholds/retries/confirmation now follow SMOVision. The old weak
  collection rescue and automatic ambiguity resolution are removed. Ambiguous
  evidence with plausible candidates remains available for manual review.

## Recommended upstream changes (not implemented here)

| Recommendation | Evidence and impact | Alpha handling / acceptance for a future change |
| --- | --- | --- |
| **Adopt Aviscribe's recent macOS ScreenCaptureKit backend** | Aviscribe `e4a73df` added `MacScreenCaptureSession`, a native ScreenCaptureKit bridge, and managed/native lifecycle tests. Supplied SMOVision uses `CGWindowListCreateImage` in `MacWindowCaptureBackend`. Backend behavior and modern macOS compatibility can therefore differ. | Use SMOVision exclusively in this alpha. Port the recent Aviscribe changes upstream, including owned frame buffers, padded rows, idle frames, stream errors, bounded layout validation, disposal, packaging/signing integration, and native tests. Revalidate permission denial/grant, resize, source closure and repeated start/stop on actual macOS hardware before release. |
| Configurable moon matching context / candidate eligibility | `MoonOverlayDetector` invokes its internal matcher across the full catalog and does not accept Aviscribe's candidate policy. A global accepted identity can be ineligible in the currently selected kingdom. | Aviscribe rejects ineligible identities rather than substituting its own recognition engine or accepting a different moon. Add an optional kingdom/collection-location/candidate filter before library matching. Test duplicate names, numbered variants, hint-art collection locations and Mushroom rematches. |
| Typed unresolved OCR results | Unreadable observations expose raw text, score, reason and crop evidence, but not the full candidate set, unresolved occurrence ID, or collection presentation. | Recompute candidate suggestions using the public matcher, scope to the current kingdom, filter eligibility, and show up to three candidates. Unresolved collections use the existing “Moon collected” review label; saved legacy story-review labels still load. Expose typed unresolved occurrences with presentation and ranked candidates so consumers need not reconstruct these details. |
| Capture lifecycle/status surface | Public capture sources expose frames/errors and physical disposal completion, but not all of the old UI's state transitions or selected default-format metadata. | The adapter reports Running after the first frame and maps source failures to the existing capture status. Actual dimensions come from frames; an unspecified default frame rate remains unknown. Expose selected format and structured start/loss/permission errors. |
| Reliable package distribution | Beta.1 now supplies immutable binary/source bundles, a release manifest, notices and checksums matching its tag. Capture forks still overlap public version numbers. | Resolved upstream in the beta release; checksum-pin all release package archives and the download, with source mapping and a fresh version-specific cache. Hosted download access remains unverified; an extracted release can be provided with `-FromFeed`. |
| Broader platform and capture validation | Existing SMOVision docs distinguish compiled backends from actual macOS/Linux/device validation. | Keep all three platform targets. Physical camera/capture-card, macOS, X11, Wayland permissions, and long-running real-game capture need hardware validation. This alpha is not an all-platform release certification. |

## Developer-tool recommendations

The old Aviscribe detector benchmarks, feature exporters, trainers, threshold
searches, native backend tests, and legacy recognition-specific audits were removed
with their engine. Git history retains them. State/package smoke commands and a
new replay command remain in Aviscribe, using SMOVision's public API.

Useful upstream tools would include a reviewed observation-manifest replay runner;
negative/ambiguous/numbered-name audits; CPU/WebGPU comparison including cold start;
live latency/drop/ownership endurance checks; crop and source-discontinuity tests;
and the recent Aviscribe macOS native fixture suite. Keep recognition tuning and
its datasets in SMOVision; keep application counting and online compatibility tests
in Aviscribe. No upstream tooling was edited or added in this task.

## Validation

- Release solution build and automated application/integration tests.
- Exact package checksum verification and isolated package restore.
- Packaged model/dictionary presence, CPU OCR initialization and provider construction.
- Legacy catalog hash and every translated name, source-kind/format mapping, saved
  settings/crops/reviews, run counting and online behavior.
- Reliable sink delivery, rejected stale work, eligibility, occurrence deduplication,
  manual removal, ambiguous review, cross-kingdom hint art and hardcore multi-moons.
- Managed capture tests cover cancellation before first frame, source loss,
  restart/reopen, independent retained frames and padded row conversion.

### Historical alpha.2 results, 2026-09-18 (Windows x64)

- Release solution build: **passed, zero warnings/errors**.
- Automated tests: **103 passed** (83 Core, 20 Capture). The online loopback and
  temporary-file tests pass with normal host permissions; a sandboxed run timed
  out while checking persisted online state, so final validation ran outside that
  sandbox. No online protocol or persistence workaround was introduced.
- State smoke and package smoke: **passed**, including CPU OCR initialization.
- Windows self-contained publish: **passed**. Verified SMOVision Capture/Vision,
  packaged model/dictionary, OpenCV and ONNX native binaries. The retired
  Aviscribe capture assemblies and native bridge are absent from the fresh output.
- Owned-window capture: **passed two start/stop cycles**, capturing 1280x720 pixels,
  retaining raw snapshots, rendering gameplay crops and processing 21/19 frames
  without detector faults. Both capture and recognition disposal completed.
  This used a temporary fixture, not the user's game or camera.
- Short file replays: repeated full-frame development stills encoded as two-second,
  20-frame clips through FFmpeg and SMOVision Media. Normal collection frame
  `point-normal-event-0038.40.png` produced Metro moon 10 collected; adaptive
  Talkatoo frame `point-662.png` produced Metro moon 28 hinted. Both matched
  expected facts with zero detector faults and complete scheduled coverage.
  CPU and preferred-GPU runs agreed. The final Talkatoo replay reported
  `WebGPU + CPU` on NVIDIA, with no fallback reason. These are integration
  checks on repeated stills, not a full-video recall or accuracy result.
- The generated clips, replay JSON and Windows publish are ignored local artifacts
  under `.local/`; recordings and binaries were not committed.

Physical device capture, full-gameplay accuracy, interactive UI acceptance,
macOS/Linux live capture and hosted CI/release distribution remain unverified.

### Beta.1 upgrade, 2026-10-26 (Windows x64)

- The release bundle manifest identifies the same commit as `v0.1.0-beta.1`.
  GitHub returned not found through the available clients during this update;
  validation used the locally cached release ZIP, verified against its recorded
  SHA-256 `82ca649228afe3fc024d32911fd2719dcc9746d48f47b8fb9f445e85ec4f7a02`.
  All eight package hashes are checked before feed preparation.
- Switched to the combined capture provider and language-specific OCR options.
  Only the existing three detector types are enabled; the beta's additional
  detectors do not add application features.
- The beta removes SMOVision.Media. Developer replay vendors the exact tagged
  FFmpeg sample decoder (namespace and provenance header only), then uses the
  same `SmoGameSession` path as live capture. No alpha dependency remains.
- The OCR model is now official PP-OCRv6_small_rec with its matching dictionary;
  the package smoke check verifies hashes and runs Traditional Chinese CPU inference.
- Beta code and the sample helper are AGPL-3.0-or-later. Package notices and the
  copied helper's license are retained; see `docs/dependencies.md`. Release
  publication and corresponding-source preparation remain separate follow-up work.
- Beta still documents CoreGraphics macOS capture. The ScreenCaptureKit
  recommendation and hardware/platform validation limitations remain open.

Beta validation results:

- Release solution build: **zero warnings/errors**. All **103 tests passed**
  (83 Core, 20 Capture), including catalog translations/hash and saved-state checks.
- All eight restored package archives match the release hashes. All active project
  dependency graphs contain only beta SMOVision packages. Inherited fallback caches
  are disabled so the pinned capture forks cannot be replaced by cached public builds.
- State smoke and packaged-asset/real CPU inference smoke: **passed**.
- Existing collection and adaptive Talkatoo repeated-still replays: **passed** with
  the unchanged expected facts on CPU (Metro 10 collected and Metro 28 hinted).
  Talkatoo also passed with **WebGPU + CPU** on NVIDIA, no fallback reason. No
  detector faults or fact differences were observed in these small fixtures;
  broader recognition accuracy with the new model remains unverified.
- Owned-window capture: **two successful 1280x720 start/stop cycles**, retained
  snapshots and crop rendering, 21/19 processed frames, zero detector faults.
- Fresh Windows publish: **passed**; beta assemblies and model/dictionary are
  present, retired Aviscribe capture assemblies and SMOVision.Media are absent.
- GitHub's live download path, hosted CI, physical capture hardware, interactive
  application acceptance, and macOS/Linux native behavior remain unverified.
  No commit, push, release publication, or SMOVision repository edit was performed.

### Input language support

English (`en`), Traditional Chinese (`zh-Hant`), and Simplified Chinese (`zh-Hans`)
are enabled in the game-text selector. Language selection is persisted through the
existing settings field and restarts the recognition session with SMOVision's
matching detector profile and language-specific OCR dimensions. Traditional
Chinese remains the default for existing installations. Output languages are
unchanged. The package smoke command performs CPU inference for all three inputs.

Validation: Release build passed with zero warnings/errors, all 103 tests passed,
and CPU OCR inference succeeded for all three supported input languages.

### Beta.2 upgrade, 2026-10-26 (Windows x64)

- Exact release commit: `495085482552e1fc1b847397c070009220adda90`.
  Used the local release bundle, verified with SHA-256
  `4bd9324c64608e1534759a40171c98c15229832cd1a4a5cd91318ef37f375b34`.
  Nine package archives, including the new optional `SMOVision.Assets`, are pinned
  by checksum. The feed and package cache use a separate beta.2 directory.
- The UI opens preview streams through `MoonImages.OpenRead`, using Aviscribe's
  compatibility mapping for owning kingdom and moon ID. Bitmap caching, disposal,
  the image visibility setting, and pending-list behavior remain unchanged.
- All 836 former Aviscribe PNGs match the package manifest byte-for-byte. A
  regression test opens every packaged image through the same identity mapping
  as the UI and verifies the aggregate of their hashes against the original files.
  The duplicate PNGs and their Avalonia resource entry are removed.
- Windows, macOS and Linux packaging checks now require `SMOVision.Assets.dll`.
  Images are embedded in that assembly; no image download or loose PNG deployment
  is needed. The main app icon remains in Aviscribe.
- Recognition code is unchanged between the beta.1 and beta.2 source tags. The
  vendored FFmpeg helper retains its accurate beta.1 source attribution.

Beta.2 validation: Release build passed with zero warnings/errors; **104 tests
passed** (83 Core, 21 Capture/UI integration), including all 836 image streams.
State smoke and CPU OCR inference passed for English, Traditional Chinese, and
Simplified Chinese. All nine restored package archives match their pinned hashes.
A fresh Windows publish contains the exact packaged `SMOVision.Assets.dll`; its
`Aviscribe.UI.dll` is 368,640 bytes and no longer embeds the 60.18 MiB of duplicate
moon PNGs. Native macOS/Linux validation and hosted download access remain unverified.
No SMOVision source files were changed, and no commit or push was made.
