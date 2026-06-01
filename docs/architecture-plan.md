# Aviscribe Architecture Plan

Aviscribe is a Talkatoo% assistant for Super Mario Odyssey. Its job is to watch a capture feed, detect when relevant moon text appears, run OCR only when useful, match the recognized text against the moon list, and maintain the run state.

## Category Rules

Standard Talkatoo%:

- Story moons count automatically toward the kingdom exit requirement.
- Multi moons are story moons worth 3 moons.
- Non-story moons count only after Talkatoo has mentioned them.

Hardcore:

- Story moons are not allowed to count.
- Non-story moons still require a Talkatoo mention.

Longer categories:

- Postgame kingdoms must be supported behind a setting.
- Hint art moons need both source kingdom and collection kingdom handling. The JSON stores the source in `kingdom` and the collection location in `collection_kingdom`.

## Project Boundaries

Shared projects should stay platform-neutral:

- `Core`: moon data, run state, OCR abstractions, matching, frame processing, classifier interfaces.
- `Core.Capture`: capture interfaces only.
- `UI`: platform-neutral Avalonia UI.

OS-specific capture belongs in OS-specific projects:

- `Windows.Capture`: Windows camera/window capture. OBS Virtual Camera compatibility requires DirectShow support because OBS Virtual Camera supports DirectShow but not Media Foundation.
- Future Linux/macOS capture providers should live in their own projects and implement `IVideoProvider` / `IVideoCapture`.

## Detection Pipeline

The intended runtime pipeline is:

1. Capture frame.
2. Crop known regions of interest.
3. Run cheap OpenCV gates.
4. Run tiny CPU classifiers for ambiguous/possible crops.
5. Require temporal stability before OCR.
6. Queue OCR only once per distinct stable text image.
7. Match OCR text against context-specific candidate moons.
8. Update pending/collected/count state.

Text regions:

- `Talkatoo`: Talkatoo's one-line moon recommendation.
- `MoonGet`: normal moon-get title.
- `StoryMoon`: story moon title. This needs its own detector because the text is diagonal and appears in a different full-screen composition.

## Classifier Workflow

The tracked classifier workspace is `Classifier/`. It reads local data from:

```text
[removed]
```

The first goal is not training; it is measurement:

1. Summarize current label counts and sample dimensions.
2. Benchmark current OpenCV gates against labeled `Talkatoo` and `MoonGet` crops.
3. Create manifest files for train/validation/test splits.
4. Run high-recall threshold experiments and export `Core/Data/detector-rules.json`.
5. Add manual correction without moving original images.
6. Train small CPU-first binary classifiers for `Talkatoo`, `MoonGet`, and `StoryMoon` if threshold rules are not good enough.
7. Export classifiers to ONNX and load them from `Core`.

`story-crops` creates a first-pass lower-screen crop from the full-frame StoryMoon samples. That crop is intentionally broad; once we inspect false positives/negatives, it can be tightened or replaced with deskewing.

The threshold rule path is deliberately an intermediate step. It lets the runtime use the exact same feature extractor as the classifier tooling, and it can be replaced by an ONNX classifier behind the same `ITextPresenceDetector` interface.

The current measured result is useful but uneven:

- Talkatoo separates well with cheap features. Runtime exports currently include a Talkatoo detector.
- MoonGet does not separate well with the current global features; high-recall rules and the linear feature model both produce too many false positives. MoonGet should move to either better localized features or a small image classifier.

The Desktop data can remain where it is or be mirrored into `Classifier/Data`. Large videos, generated CSVs, and model experiments are ignored unless a final small model is intentionally promoted into `Core/Data`.

## Near-Term Implementation Order

1. Finish run-rule support in `Core`.
2. Add classifier interfaces and a frame state machine in `Core`.
3. Benchmark the current detectors with `Classifier`.
4. Add StoryMoon ROI extraction/normalization from the existing full-frame story samples.
5. Train/export CPU classifiers.
6. Replace heuristic OCR triggering with classifier-backed triggering.
7. Expand the UI for language, category, postgame, kingdom, pending/collected moons, correction controls, and OBS text output.
