using Aviscribe.Classifier;
using Aviscribe.Core.Ocr;

var command = args.Length > 0 ? args[0].ToLowerInvariant() : "summary";
try
{
    switch (command)
    {
        case "summary":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            DatasetInspector.PrintSummary(dataRoot);
            break;
        }

        case "benchmark":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            DetectorBenchmark.PrintSummary(dataRoot);
            break;
        }

        case "manifest":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            DatasetManifest.Write(
                dataRoot,
                args.Length > 2 ? args[2] : DatasetPaths.DefaultManifestPath);
            break;
        }

        case "features":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            FeatureExporter.Write(
                dataRoot,
                args.Length > 2 ? args[2] : DatasetPaths.DefaultFeaturesPath);
            break;
        }

        case "thresholds":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            var minimumRecall = args.Length > 2 && double.TryParse(args[2], out var parsedRecall)
                ? parsedRecall
                : 0.995;
            ThresholdExperiment.PrintSummary(dataRoot, minimumRecall);
            break;
        }

        case "rules":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            var outputPath = args.Length > 2 ? args[2] : Path.Combine("Core", "Data", "detector-rules.json");
            var minimumRecall = args.Length > 3 && double.TryParse(args[3], out var parsedRecall)
                ? parsedRecall
                : 0.995;
            var maximumFalsePositiveRate = args.Length > 4 && double.TryParse(args[4], out var parsedFalsePositiveRate)
                ? parsedFalsePositiveRate
                : 0.05;
            ThresholdRuleExporter.Write(dataRoot, outputPath, minimumRecall, maximumFalsePositiveRate);
            break;
        }

        case "train-linear":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            var outputPath = args.Length > 2 ? args[2] : Path.Combine("Core", "Data", "linear-detector.json");
            var minimumRecall = args.Length > 3 && double.TryParse(args[3], out var parsedRecall)
                ? parsedRecall
                : 0.995;
            var maximumFalsePositiveRate = args.Length > 4 && double.TryParse(args[4], out var parsedFalsePositiveRate)
                ? parsedFalsePositiveRate
                : 0.05;
            LinearModelTrainer.TrainAndWrite(dataRoot, outputPath, minimumRecall, maximumFalsePositiveRate);
            break;
        }

        case "extract":
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: extract <videoPath> <outputDir> [modulo] [full|regions]");
                return 1;
            }

            var modulo = args.Length > 3 && int.TryParse(args[3], out var parsedModulo)
                ? parsedModulo
                : 10;
            var mode = args.Length > 4 ? args[4].ToLowerInvariant() : "regions";
            FrameExtractor.Extract(args[1], args[2], modulo, mode == "full" ? null : RoiDefinitions.StandardTextRegions);
            break;

        case "story-crops":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("Classifier", "Data", "StoryMoonCrops");
            StoryMoonCropper.Write(dataRoot, outputDir);
            break;
        }

        case "audit-talkatoo-video":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("Classifier", "Output", "TalkatooAudit");
            var stride = args.Length > 3 && int.TryParse(args[3], out var parsedStride)
                ? parsedStride
                : 1;
            var maxSaved = args.Length > 4 && int.TryParse(args[4], out var parsedMaxSaved)
                ? parsedMaxSaved
                : 80;
            var maxFrames = args.Length > 5 && int.TryParse(args[5], out var parsedMaxFrames)
                ? parsedMaxFrames
                : 0;
            TalkatooVideoAudit.Run(videoPath, outputDir, stride, maxSaved, maxFrames);
            break;
        }

        case "audit-moonget-video":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("Classifier", "Output", "MoonGetVideoAudit");
            var stride = args.Length > 3 && int.TryParse(args[3], out var parsedStride)
                ? parsedStride
                : 1;
            var maxSaved = args.Length > 4 && int.TryParse(args[4], out var parsedMaxSaved)
                ? parsedMaxSaved
                : 80;
            var maxFrames = args.Length > 5 && int.TryParse(args[5], out var parsedMaxFrames)
                ? parsedMaxFrames
                : 0;
            RegionVideoAudit.Run(
                OcrRegionType.MoonGet,
                new OpenCvSharp.Rect(320, 600, 1250, 250),
                stableFrameCount: 3,
                stableImageMaxHammingDistance: 64,
                videoPath,
                outputDir,
                stride,
                maxSaved,
                maxFrames);
            break;
        }

        case "audit-storymoon-video":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("Classifier", "Output", "StoryMoonVideoAudit");
            var stride = args.Length > 3 && int.TryParse(args[3], out var parsedStride)
                ? parsedStride
                : 1;
            var maxSaved = args.Length > 4 && int.TryParse(args[4], out var parsedMaxSaved)
                ? parsedMaxSaved
                : 80;
            var maxFrames = args.Length > 5 && int.TryParse(args[5], out var parsedMaxFrames)
                ? parsedMaxFrames
                : 0;
            RegionVideoAudit.Run(
                OcrRegionType.StoryMoon,
                new OpenCvSharp.Rect(450, 820, 1100, 150),
                stableFrameCount: 12,
                stableImageMaxHammingDistance: 12,
                videoPath,
                outputDir,
                stride,
                maxSaved,
                maxFrames);
            break;
        }

        case "audit-talkatoo-dataset":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("Classifier", "Output", "TalkatooDatasetAudit");
            var maxPerBucket = args.Length > 3 && int.TryParse(args[3], out var parsedMax)
                ? parsedMax
                : 80;
            TalkatooDatasetAudit.Run(dataRoot, outputDir, maxPerBucket);
            break;
        }

        case "audit-detector-failures":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            var region = args.Length > 2 ? args[2] : "Talkatoo";
            var outputDir = args.Length > 3 ? args[3] : Path.Combine("Classifier", "Output", $"{region}Failures");
            var maxSaved = args.Length > 4 && int.TryParse(args[4], out var parsedMax)
                ? parsedMax
                : 80;
            DetectorFailureAudit.Run(dataRoot, region, outputDir, maxSaved);
            break;
        }

        case "inspect-talkatoo":
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: inspect-talkatoo <imagePath> [imagePath...]");
                return 1;
            }

            TalkatooInspector.Print(args.Skip(1));
            break;
        }

        case "talkatoo-projection-search":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            TalkatooProjectionExperiment.PrintSummary(dataRoot);
            break;
        }

        case "moonget-search":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            MoonGetExperiment.PrintSummary(dataRoot);
            break;
        }

        case "inspect-moonget":
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: inspect-moonget <imagePath> [imagePath...]");
                return 1;
            }

            MoonGetInspector.Print(args.Skip(1));
            break;
        }

        case "storymoon-search":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            StoryMoonExperiment.PrintSummary(dataRoot);
            break;
        }

        case "state-smoke":
        {
            StateSmoke.Run();
            break;
        }

        default:
            PrintUsage();
            return 1;
    }

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

static void PrintUsage()
{
    Console.WriteLine("Aviscribe.Classifier commands:");
    Console.WriteLine("  summary [dataRoot]");
    Console.WriteLine("  benchmark [dataRoot]");
    Console.WriteLine("  manifest [dataRoot] [outputCsv]");
    Console.WriteLine("  features [dataRoot] [outputCsv]");
    Console.WriteLine("  thresholds [dataRoot] [minimumRecall]");
    Console.WriteLine("  rules [dataRoot] [outputJson] [minimumRecall] [maximumFalsePositiveRate]");
    Console.WriteLine("  train-linear [dataRoot] [outputJson] [minimumRecall] [maximumFalsePositiveRate]");
    Console.WriteLine("  extract <videoPath> <outputDir> [modulo] [full|regions]");
    Console.WriteLine("  story-crops [dataRoot] [outputDir]");
    Console.WriteLine("  audit-talkatoo-video [videoPath] [outputDir] [stride] [maxSaved] [maxFrames]");
    Console.WriteLine("  audit-moonget-video [videoPath] [outputDir] [stride] [maxSaved] [maxFrames]");
    Console.WriteLine("  audit-storymoon-video [videoPath] [outputDir] [stride] [maxSaved] [maxFrames]");
    Console.WriteLine("  audit-talkatoo-dataset [dataRoot] [outputDir] [maxPerBucket]");
    Console.WriteLine("  audit-detector-failures [dataRoot] [Talkatoo|MoonGet] [outputDir] [maxSaved]");
    Console.WriteLine("  inspect-talkatoo <imagePath> [imagePath...]");
    Console.WriteLine("  talkatoo-projection-search [dataRoot]");
    Console.WriteLine("  moonget-search [dataRoot]");
    Console.WriteLine("  inspect-moonget <imagePath> [imagePath...]");
    Console.WriteLine("  storymoon-search [dataRoot]");
    Console.WriteLine("  state-smoke");
}
