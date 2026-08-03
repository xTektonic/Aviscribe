using Aviscribe.Classifier;
using Aviscribe.Core;
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

        case "ocr-provider-benchmark":
        {
            OcrProviderBenchmark.Run();
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
            var outputPath = args.Length > 2 ? args[2] : Path.Combine("src", "Aviscribe.Core", "Data", "detector-rules.json");
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
            var outputPath = args.Length > 2 ? args[2] : Path.Combine("src", "Aviscribe.Core", "Data", "linear-detector.json");
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

        case "video-info":
        {
            if (args.Length < 2)
            {
                Console.Error.WriteLine("Usage: video-info <videoPath>");
                return 1;
            }

            VideoSampler.PrintInfo(args[1]);
            break;
        }

        case "sample-video-grid":
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: sample-video-grid <videoPath> <outputDir> [stepSeconds] [maxSamples]");
                return 1;
            }

            var stepSeconds = args.Length > 3 && double.TryParse(args[3], out var parsedStepSeconds)
                ? parsedStepSeconds
                : 30;
            var maxSamples = args.Length > 4 && int.TryParse(args[4], out var parsedMaxSamples)
                ? parsedMaxSamples
                : 0;
            VideoSampler.WriteGrid(args[1], args[2], stepSeconds, maxSamples);
            break;
        }

        case "extract-video-frames":
        {
            if (args.Length < 4)
            {
                Console.Error.WriteLine("Usage: extract-video-frames <videoPath> <outputDir> <frame> [frame...]");
                return 1;
            }

            VideoSampler.WriteFrames(args[1], args[2], args.Skip(3).Select(int.Parse));
            break;
        }

        case "inspect-kingdom":
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine(
                    "Usage: inspect-kingdom <templateDirectory> <imagePath> [imagePath...]");
                return 1;
            }

            KingdomDetectionInspector.Print(args[1], args.Skip(2));
            break;
        }

        case "kingdom-video-regression":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(
                    DatasetPaths.DefaultDataRoot,
                    "talkatoo_all_moons.mp4");
            var templateDirectory = args.Length > 2
                ? args[2]
                : AppPaths.KingdomIconTemplateFolder;
            KingdomDetectionVideoRegressionSuite.Run(
                videoPath,
                templateDirectory);
            break;
        }

        case "mine-video-events":
        {
            if (args.Length < 6)
            {
                Console.Error.WriteLine("Usage: mine-video-events <videoPath> <outputDir> <Talkatoo|MoonGet|StoryMoon> <startFrame> <maxFrames> [stride] [maxRuns] [kingdom]");
                return 1;
            }

            if (!Enum.TryParse<OcrRegionType>(args[3], ignoreCase: true, out var regionType))
            {
                Console.Error.WriteLine("Region must be Talkatoo, MoonGet, or StoryMoon.");
                return 1;
            }

            var startFrame = int.Parse(args[4]);
            var maxFrames = int.Parse(args[5]);
            var stride = args.Length > 6 && int.TryParse(args[6], out var parsedStride)
                ? parsedStride
                : 2;
            var maxRuns = args.Length > 7 && int.TryParse(args[7], out var parsedMaxRuns)
                ? parsedMaxRuns
                : 40;
            var kingdom = args.Length > 8 ? args[8] : null;

            VideoEventMiner.Run(args[1], args[2], regionType, startFrame, maxFrames, stride, maxRuns, kingdom);
            break;
        }

        case "suspicious-video-events":
        {
            if (args.Length < 6)
            {
                Console.Error.WriteLine("Usage: suspicious-video-events <videoPath> <outputDir> <Talkatoo|MoonGet|StoryMoon> <startFrame> <maxFrames> [stride] [maxSaved] [minimumScore] [kingdom]");
                return 1;
            }

            if (!Enum.TryParse<OcrRegionType>(args[3], ignoreCase: true, out var regionType))
            {
                Console.Error.WriteLine("Region must be Talkatoo, MoonGet, or StoryMoon.");
                return 1;
            }

            var startFrame = int.Parse(args[4]);
            var maxFrames = int.Parse(args[5]);
            var stride = args.Length > 6 && int.TryParse(args[6], out var parsedStride)
                ? parsedStride
                : 2;
            var maxSaved = args.Length > 7 && int.TryParse(args[7], out var parsedMaxSaved)
                ? parsedMaxSaved
                : 40;
            var minimumScore = args.Length > 8 && double.TryParse(args[8], out var parsedMinimumScore)
                ? parsedMinimumScore
                : 0.70;
            var kingdom = args.Length > 9 ? args[9] : null;

            SuspiciousVideoEventAudit.Run(
                args[1],
                args[2],
                regionType,
                startFrame,
                maxFrames,
                stride,
                maxSaved,
                minimumScore,
                kingdom);
            break;
        }

        case "ocr-oracle-video-audit":
        {
            if (args.Length < 6)
            {
                Console.Error.WriteLine("Usage: ocr-oracle-video-audit <videoPath> <outputDir> <Talkatoo|MoonGet|StoryMoon> <startFrame> <maxFrames> [stride] [maxSaved] [minimumScore] [kingdom]");
                return 1;
            }

            if (!Enum.TryParse<OcrRegionType>(args[3], ignoreCase: true, out var regionType))
            {
                Console.Error.WriteLine("Region must be Talkatoo, MoonGet, or StoryMoon.");
                return 1;
            }

            var startFrame = int.Parse(args[4]);
            var maxFrames = int.Parse(args[5]);
            var stride = args.Length > 6 && int.TryParse(args[6], out var parsedStride)
                ? parsedStride
                : 3;
            var maxSaved = args.Length > 7 && int.TryParse(args[7], out var parsedMaxSaved)
                ? parsedMaxSaved
                : 80;
            var minimumScore = args.Length > 8 && double.TryParse(args[8], out var parsedMinimumScore)
                ? parsedMinimumScore
                : 0.70;
            var kingdom = args.Length > 9 ? args[9] : null;

            OcrOracleVideoAudit.Run(
                args[1],
                args[2],
                regionType,
                startFrame,
                maxFrames,
                stride,
                maxSaved,
                minimumScore,
                kingdom);
            break;
        }

        case "video-regression":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("tools", "Aviscribe.Classifier", "Output", "VideoRegressionFailures");
            VideoRegressionSuite.Run(videoPath, outputDir);
            break;
        }

        case "ocr-probe":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            VideoOcrProbe.Print(videoPath, ProgramHelpers.VideoOcrRegressionRequests());
            break;
        }

        case "video-ocr-regression":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            VideoOcrProbe.AssertMatches(videoPath, ProgramHelpers.VideoOcrRegressionRequests());
            break;
        }

        case "verify-video-pipeline":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputRoot = args.Length > 2
                ? args[2]
                : Path.Combine("tools", "Aviscribe.Classifier", "Output", "VideoPipelineVerification");
            VideoPipelineVerifier.Run(videoPath, outputRoot);
            break;
        }

        case "video-e2e-regression":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputDir = args.Length > 2
                ? args[2]
                : Path.Combine("tools", "Aviscribe.Classifier", "Output", "VideoEndToEndFailures");
            VideoEndToEndRegressionSuite.Run(videoPath, outputDir);
            break;
        }

        case "all-moons-regression":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "talkatoo_all_moons.mp4");
            var outputDir = args.Length > 2
                ? args[2]
                : Path.Combine("tools", "Aviscribe.Classifier", "Output", "AllMoonsRegressionFailures");
            AllMoonsVideoRegressionSuite.Run(videoPath, outputDir);
            break;
        }

        case "all-moons-frameprocessor-regression":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "talkatoo_all_moons.mp4");
            AllMoonsFrameProcessorRegressionSuite.Run(videoPath);
            break;
        }

        case "frameprocessor-video-regression":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            FrameProcessorVideoRegressionSuite.Run(videoPath);
            break;
        }

        case "frameprocessor-chronological-regression":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            FrameProcessorVideoRegressionSuite.RunChronological(videoPath);
            break;
        }

        case "mine-overlay-events":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("tools", "Aviscribe.Classifier", "Output", "OverlayEvents");
            var stride = args.Length > 3 && int.TryParse(args[3], out var parsedStride)
                ? parsedStride
                : 15;
            var maxFrames = args.Length > 4 && int.TryParse(args[4], out var parsedMaxFrames)
                ? parsedMaxFrames
                : 0;
            var minGap = args.Length > 5 && int.TryParse(args[5], out var parsedMinGap)
                ? parsedMinGap
                : 90;
            OverlayEventMiner.Mine(videoPath, outputDir, stride, maxFrames, minGap);
            break;
        }

        case "overlay-coverage":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("tools", "Aviscribe.Classifier", "Output", "OverlayCoverage");
            var stride = args.Length > 3 && int.TryParse(args[3], out var parsedStride)
                ? parsedStride
                : 15;
            var maxFrames = args.Length > 4 && int.TryParse(args[4], out var parsedMaxFrames)
                ? parsedMaxFrames
                : 0;
            var minGap = args.Length > 5 && int.TryParse(args[5], out var parsedMinGap)
                ? parsedMinGap
                : 90;
            var window = args.Length > 6 && int.TryParse(args[6], out var parsedWindow)
                ? parsedWindow
                : 150;
            OverlayEventMiner.AuditCoverage(videoPath, outputDir, stride, maxFrames, minGap, window);
            break;
        }

        case "story-crops":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("tools", "Aviscribe.Classifier", "Data", "StoryMoonCrops");
            StoryMoonCropper.Write(dataRoot, outputDir);
            break;
        }

        case "audit-talkatoo-video":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("tools", "Aviscribe.Classifier", "Output", "TalkatooAudit");
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
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("tools", "Aviscribe.Classifier", "Output", "MoonGetVideoAudit");
            var stride = args.Length > 3 && int.TryParse(args[3], out var parsedStride)
                ? parsedStride
                : 1;
            var maxSaved = args.Length > 4 && int.TryParse(args[4], out var parsedMaxSaved)
                ? parsedMaxSaved
                : 80;
            var maxFrames = args.Length > 5 && int.TryParse(args[5], out var parsedMaxFrames)
                ? parsedMaxFrames
                : 0;
            var startFrame = args.Length > 6 && int.TryParse(args[6], out var parsedStartFrame)
                ? parsedStartFrame
                : 0;
            RegionVideoAudit.Run(
                CollectionConfirmationProfile.MoonGet,
                videoPath,
                outputDir,
                stride,
                maxSaved,
                maxFrames,
                startFrame);
            break;
        }

        case "audit-storymoon-video":
        {
            var videoPath = args.Length > 1
                ? args[1]
                : Path.Combine(DatasetPaths.DefaultDataRoot, "sampling_video.mp4");
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("tools", "Aviscribe.Classifier", "Output", "StoryMoonVideoAudit");
            var stride = args.Length > 3 && int.TryParse(args[3], out var parsedStride)
                ? parsedStride
                : 1;
            var maxSaved = args.Length > 4 && int.TryParse(args[4], out var parsedMaxSaved)
                ? parsedMaxSaved
                : 80;
            var maxFrames = args.Length > 5 && int.TryParse(args[5], out var parsedMaxFrames)
                ? parsedMaxFrames
                : 0;
            var startFrame = args.Length > 6 && int.TryParse(args[6], out var parsedStartFrame)
                ? parsedStartFrame
                : 0;
            RegionVideoAudit.Run(
                CollectionConfirmationProfile.StoryMoon,
                videoPath,
                outputDir,
                stride,
                maxSaved,
                maxFrames,
                startFrame);
            break;
        }

        case "audit-talkatoo-dataset":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            var outputDir = args.Length > 2 ? args[2] : Path.Combine("tools", "Aviscribe.Classifier", "Output", "TalkatooDatasetAudit");
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
            var outputDir = args.Length > 3 ? args[3] : Path.Combine("tools", "Aviscribe.Classifier", "Output", $"{region}Failures");
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

        case "inspect-talkatoo-video":
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: inspect-talkatoo-video <videoPath> <frame> [frame...]");
                return 1;
            }

            TalkatooInspector.PrintVideoFrames(args[1], args.Skip(2).Select(int.Parse));
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

        case "inspect-moonget-video":
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("Usage: inspect-moonget-video <videoPath> <frame> [frame...]");
                return 1;
            }

            MoonGetInspector.PrintVideoFrames(args[1], args.Skip(2).Select(int.Parse));
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

        case "frameprocessor-smoke":
        {
            FrameProcessorSmoke.Run();
            break;
        }

        case "capture-crop-smoke":
        {
            CaptureCropSmoke.Run();
            break;
        }

        case "talkatoo-confirmation-smoke":
        {
            TalkatooConfirmationSmoke.Run();
            break;
        }

        case "talkatoo-static-gate-audit":
        {
            var dataRoot = args.Length > 1 ? args[1] : DatasetPaths.DefaultDataRoot;
            TalkatooStaticGateAudit.Run(dataRoot);
            break;
        }

        case "matcher-smoke":
        {
            MatcherSmoke.Run();
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
    Console.WriteLine("  video-info <videoPath>");
    Console.WriteLine("  sample-video-grid <videoPath> <outputDir> [stepSeconds] [maxSamples]");
    Console.WriteLine("  extract-video-frames <videoPath> <outputDir> <frame> [frame...]");
    Console.WriteLine("  inspect-kingdom <templateDirectory> <imagePath> [imagePath...]");
    Console.WriteLine("  kingdom-video-regression [videoPath] [templateDirectory]");
    Console.WriteLine("  mine-video-events <videoPath> <outputDir> <Talkatoo|MoonGet|StoryMoon> <startFrame> <maxFrames> [stride] [maxRuns] [kingdom]");
    Console.WriteLine("  suspicious-video-events <videoPath> <outputDir> <Talkatoo|MoonGet|StoryMoon> <startFrame> <maxFrames> [stride] [maxSaved] [minimumScore] [kingdom]");
    Console.WriteLine("  ocr-oracle-video-audit <videoPath> <outputDir> <Talkatoo|MoonGet|StoryMoon> <startFrame> <maxFrames> [stride] [maxSaved] [minimumScore] [kingdom]");
    Console.WriteLine("  video-regression [videoPath] [failureOutputDir]");
    Console.WriteLine("  ocr-probe [videoPath]");
    Console.WriteLine("  video-ocr-regression [videoPath]");
    Console.WriteLine("  verify-video-pipeline [videoPath] [outputRoot]");
    Console.WriteLine("  video-e2e-regression [videoPath] [failureOutputDir]");
    Console.WriteLine("  all-moons-regression [videoPath] [failureOutputDir]");
    Console.WriteLine("  all-moons-frameprocessor-regression [videoPath]");
    Console.WriteLine("  frameprocessor-video-regression [videoPath]");
    Console.WriteLine("  frameprocessor-chronological-regression [videoPath]");
    Console.WriteLine("  mine-overlay-events [videoPath] [outputDir] [strideFrames] [maxFrames] [minGapFrames]");
    Console.WriteLine("  overlay-coverage [videoPath] [outputDir] [strideFrames] [maxFrames] [minGapFrames] [windowFrames]");
    Console.WriteLine("  story-crops [dataRoot] [outputDir]");
    Console.WriteLine("  audit-talkatoo-video [videoPath] [outputDir] [stride] [maxSaved] [maxFrames]");
    Console.WriteLine("  audit-moonget-video [videoPath] [outputDir] [stride] [maxSaved] [maxFrames] [startFrame]");
    Console.WriteLine("  audit-storymoon-video [videoPath] [outputDir] [stride] [maxSaved] [maxFrames] [startFrame]");
    Console.WriteLine("  audit-talkatoo-dataset [dataRoot] [outputDir] [maxPerBucket]");
    Console.WriteLine("  audit-detector-failures [dataRoot] [Talkatoo|MoonGet] [outputDir] [maxSaved]");
    Console.WriteLine("  inspect-talkatoo <imagePath> [imagePath...]");
    Console.WriteLine("  inspect-talkatoo-video <videoPath> <frame> [frame...]");
    Console.WriteLine("  talkatoo-projection-search [dataRoot]");
    Console.WriteLine("  moonget-search [dataRoot]");
    Console.WriteLine("  inspect-moonget <imagePath> [imagePath...]");
    Console.WriteLine("  inspect-moonget-video <videoPath> <frame> [frame...]");
    Console.WriteLine("  storymoon-search [dataRoot]");
    Console.WriteLine("  state-smoke");
    Console.WriteLine("  frameprocessor-smoke");
    Console.WriteLine("  capture-crop-smoke");
    Console.WriteLine("  talkatoo-confirmation-smoke");
    Console.WriteLine("  talkatoo-static-gate-audit [dataRoot]");
    Console.WriteLine("  matcher-smoke");
}
