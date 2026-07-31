# Aviscribe Classifier

This project is the tracked home for classifier experiments and detector benchmarks.
It intentionally reads the existing local data by path instead of moving or rewriting it.

Default data root:

```text
C:\Users\amaho\Desktop\AviscribeClassifierData
```

Current roles:

- `Talkatoo`: cropped Talkatoo moon text region.
- `MoonGet`: cropped normal moon-get text region.
- `StoryMoons`: uncategorized full-frame samples from an All Story Moons run. These need a separate ROI/normalization path because the text is diagonal.

Near-term workflow:

1. Use this project to summarize label counts and benchmark the current OpenCV gates.
2. Create a manifest-based dataset split so mislabeled samples can be corrected without moving files.
3. Train and export small CPU-first binary ONNX classifiers for `Talkatoo`, `MoonGet`, and `StoryMoon`.
4. Feed classifier confidence into the core frame state machine before OCR is queued.

## Commands

Run from the repository root:

```text
dotnet run --project tools/Aviscribe.Classifier -- summary
dotnet run --project tools/Aviscribe.Classifier -- benchmark
dotnet run --project tools/Aviscribe.Classifier -- manifest
dotnet run --project tools/Aviscribe.Classifier -- features
dotnet run --project tools/Aviscribe.Classifier -- thresholds
dotnet run --project tools/Aviscribe.Classifier -- rules
dotnet run --project tools/Aviscribe.Classifier -- train-linear
dotnet run --project tools/Aviscribe.Classifier -- extract C:\path\run.mp4 tools\Aviscribe.Classifier\Data\Extracted 10 regions
dotnet run --project tools/Aviscribe.Classifier -- story-crops
```

`tools\Aviscribe.Classifier\Data`, `tools\Aviscribe.Classifier\Output`, and
`tools\Aviscribe.Classifier\Models` are ignored so local data mirrors, generated
CSVs, and experimental models can live with the solution without being committed accidentally.

`thresholds`, `rules`, and `train-linear` optimize for high recall by default because a false negative can miss a moon title entirely. Runtime exports only include regions that also stay under the false-positive cap, defaulting to 5%. Threshold rules are written to `src\Aviscribe.Core\Data\detector-rules.json`; linear models are written to `src\Aviscribe.Core\Data\linear-detector.json`.
