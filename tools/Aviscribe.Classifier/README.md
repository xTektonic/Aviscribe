# Aviscribe Classifier

Aviscribe.Classifier contains maintainer tools for inspecting datasets,
benchmarking text detectors, extracting video frames, auditing OCR events, and
exporting detector data used by the application. It is not required to run the
desktop application.

## Data

Commands use the repository-relative tools/Aviscribe.Classifier/Data directory
when no path is supplied. Pass another data directory as the command's first
argument when needed. The tool reads local datasets and does not move or rewrite
the source images. Its generated Data, Output, and Models directories are
ignored by Git.

## Common commands

Run these from the repository root:

~~~text
dotnet run --project tools/Aviscribe.Classifier -- summary [dataRoot]
dotnet run --project tools/Aviscribe.Classifier -- benchmark [dataRoot]
dotnet run --project tools/Aviscribe.Classifier -- manifest [dataRoot] [outputCsv]
dotnet run --project tools/Aviscribe.Classifier -- features [dataRoot] [outputCsv]
dotnet run --project tools/Aviscribe.Classifier -- thresholds [dataRoot]
dotnet run --project tools/Aviscribe.Classifier -- rules [dataRoot] [outputJson]
dotnet run --project tools/Aviscribe.Classifier -- train-linear [dataRoot] [outputJson]
dotnet run --project tools/Aviscribe.Classifier -- extract <videoPath> <outputDir> [modulo] [full|regions]
dotnet run --project tools/Aviscribe.Classifier -- story-crops [dataRoot] [outputDir]
dotnet run --project tools/Aviscribe.Classifier -- ocr-provider-benchmark
~~~

Pass an unknown command such as help to print the full command list.

Detector rules and linear models can be exported to
src/Aviscribe.Core/Data/. Review generated changes before committing them.
