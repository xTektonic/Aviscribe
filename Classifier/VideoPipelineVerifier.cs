using Aviscribe.Core.Ocr;

namespace Aviscribe.Classifier
{
    internal static class VideoPipelineVerifier
    {
        public static void Run(string videoPath, string outputRoot)
        {
            Directory.CreateDirectory(outputRoot);

            RunStep("detector video regression", () =>
                VideoRegressionSuite.Run(
                    videoPath,
                    Path.Combine(outputRoot, "VideoRegressionFailures")));

            RunStep("OCR video regression", () =>
                VideoOcrProbe.AssertMatches(videoPath, ProgramHelpers.VideoOcrRegressionRequests()));

            RunStep("end-to-end video regression", () =>
                VideoEndToEndRegressionSuite.Run(
                    videoPath,
                    Path.Combine(outputRoot, "VideoEndToEndFailures")));

            RunStep("FrameProcessor chronological video regression", () =>
                FrameProcessorVideoRegressionSuite.RunChronological(videoPath));

            RunStep("FrameProcessor smoke", FrameProcessorSmoke.Run);
            RunStep("matcher smoke", MatcherSmoke.Run);
            RunStep("state smoke", StateSmoke.Run);

            Console.WriteLine("Video pipeline verification passed.");
        }

        private static void RunStep(string name, Action action)
        {
            Console.WriteLine();
            Console.WriteLine($"=== {name} ===");
            action();
        }
    }

    internal static class ProgramHelpers
    {
        public static VideoOcrProbe.ProbeRequest[] VideoOcrRegressionRequests()
        {
            return
            [
                new("cascade-talkatoo", OcrRegionType.Talkatoo, "Cascade", 18_318, 16),
                new("cascade-talkatoo-behind-waterfall", OcrRegionType.Talkatoo, "Cascade", 18_488, 4),
                new("cascade-talkatoo-waterfall-basin", OcrRegionType.Talkatoo, "Cascade", 18_656, 6),
                new("sand-talkatoo-top-dune", OcrRegionType.Talkatoo, "Sand", 24_704, 17),
                new("sand-talkatoo-skull-sign", OcrRegionType.Talkatoo, "Sand", 27_374, 55),
                new("lake-talkatoo-secret-room", OcrRegionType.Talkatoo, "Lake", 72_004, 17),
                new("lake-talkatoo-broken-pillar", OcrRegionType.Talkatoo, "Lake", 73_050, 7),
                new("wooded-talkatoo-elevator", OcrRegionType.Talkatoo, "Wooded", 82_562, 45),
                new("wooded-talkatoo-behind-rock-wall", OcrRegionType.Talkatoo, "Wooded", 117_404, 5),
                new("lost-talkatoo-caged-gold", OcrRegionType.Talkatoo, "Lost", 133_190, 3),
                new("seaside-talkatoo-valley", OcrRegionType.Talkatoo, "Seaside", 237_596, 45),
                new("luncheon-talkatoo-fork", OcrRegionType.Talkatoo, "Luncheon", 277_182, 41),
                new("cascade-moonget-first", OcrRegionType.MoonGet, "Cascade", 10_782, 1),
                new("sand-moonget-skull-sign", OcrRegionType.MoonGet, "Sand", 32_382, 55),
                new("sand-moonget-palm-notes", OcrRegionType.MoonGet, "Sand", 57_582, 32),
                new("lake-moonget-broken-pillar", OcrRegionType.MoonGet, "Lake", 74_138, 7),
                new("wooded-moonget-fire-cave", OcrRegionType.MoonGet, "Wooded", 84_476, 19),
                new("wooded-moonget-stretching", OcrRegionType.MoonGet, "Wooded", 94_098, 25),
                new("seaside-moonget-northern", OcrRegionType.MoonGet, "Seaside", 233_982, 19),
            ];
        }
    }
}
