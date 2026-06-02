using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class FrameProcessorVideoRegressionSuite
    {
        private static readonly RuntimeExpectation[] Expectations =
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

            new("cascade-moonget-first", OcrRegionType.MoonGet, "Cascade", 10_782, 1),
            new("sand-moonget-skull-sign", OcrRegionType.MoonGet, "Sand", 32_382, 55),
            new("sand-moonget-palm-notes", OcrRegionType.MoonGet, "Sand", 57_582, 32),
            new("lake-moonget-broken-pillar", OcrRegionType.MoonGet, "Lake", 74_138, 7),
            new("wooded-moonget-fire-cave", OcrRegionType.MoonGet, "Wooded", 84_476, 19),
            new("wooded-moonget-stretching", OcrRegionType.MoonGet, "Wooded", 94_098, 25),
        ];

        public static void Run(
            string videoPath,
            int windowFrames = 42,
            int frameDelayMilliseconds = 8,
            int settleMilliseconds = 4000)
        {
            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            var repo = MoonRepository.LoadDefault();
            var failures = new List<RuntimeExpectation>();

            foreach (var expectation in Expectations)
            {
                if (!RunExpectation(
                    capture,
                    repo,
                    expectation,
                    windowFrames,
                    frameDelayMilliseconds,
                    settleMilliseconds))
                {
                    failures.Add(expectation);
                }
            }

            if (failures.Count > 0)
                throw new InvalidOperationException($"{failures.Count} frame processor video expectation(s) failed.");

            Console.WriteLine($"FrameProcessor video regression passed: {Expectations.Length} expected windows updated state.");
        }

        public static void RunChronological(
            string videoPath,
            int windowFrames = 42,
            int frameDelayMilliseconds = 8,
            int settleMilliseconds = 4000)
        {
            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            var repo = MoonRepository.LoadDefault();
            var state = new GameState();
            using var ocr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage, state.Settings.OutputLanguage);
            var processor = new FrameProcessor(ocr, matcher, state);
            var failures = new List<RuntimeExpectation>();

            processor.Start();
            try
            {
                foreach (var expectation in Expectations.OrderBy(expectation => expectation.Frame))
                {
                    var expectedMoon = FindExpectedMoon(repo, expectation);
                    if (!string.Equals(state.CurrentKingdom, expectation.Kingdom, StringComparison.OrdinalIgnoreCase))
                        state.SetKingdom(expectation.Kingdom);

                    if (expectation.RegionType != OcrRegionType.Talkatoo &&
                        !expectedMoon.IsStory &&
                        !state.CreateSnapshot().Pending.Any(moon => moon.Id == expectedMoon.Id))
                    {
                        state.AddPending(expectedMoon);
                    }

                    FeedFrames(
                        capture,
                        processor,
                        expectation.Frame - windowFrames,
                        expectation.Frame + windowFrames,
                        frameDelayMilliseconds);

                    if (WaitForExpectedMoon(state, expectation.RegionType, expectedMoon, settleMilliseconds))
                    {
                        var snapshot = state.CreateSnapshot();
                        Console.WriteLine(
                            $"PASS {expectation.Name}: kingdom {snapshot.CurrentKingdom}, " +
                            $"pending {snapshot.Pending.Count}, collected {snapshot.Collected.Count}, " +
                            $"uncounted {snapshot.UncountedCollected.Count}");
                        continue;
                    }

                    failures.Add(expectation);
                    var finalSnapshot = state.CreateSnapshot();
                    Console.WriteLine(
                        $"FAIL {expectation.Name}: kingdom {finalSnapshot.CurrentKingdom}, " +
                        $"pending [{string.Join(", ", finalSnapshot.Pending.Select(m => m.Id))}], " +
                        $"collected [{string.Join(", ", finalSnapshot.Collected.Select(m => m.Id))}], " +
                        $"uncounted [{string.Join(", ", finalSnapshot.UncountedCollected.Select(m => m.Id))}]");
                }
            }
            finally
            {
                processor.Stop();
            }

            if (failures.Count > 0)
                throw new InvalidOperationException($"{failures.Count} chronological frame processor expectation(s) failed.");

            Console.WriteLine($"FrameProcessor chronological video regression passed: {Expectations.Length} expected windows updated state.");
        }

        private static bool RunExpectation(
            VideoCapture capture,
            MoonRepository repo,
            RuntimeExpectation expectation,
            int windowFrames,
            int frameDelayMilliseconds,
            int settleMilliseconds)
        {
            var expectedMoon = FindExpectedMoon(repo, expectation);

            var state = new GameState();
            state.SetKingdom(expectation.Kingdom);
            if (expectation.RegionType != OcrRegionType.Talkatoo && !expectedMoon.IsStory)
                state.AddPending(expectedMoon);

            using var ocr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage, state.Settings.OutputLanguage);
            var processor = new FrameProcessor(ocr, matcher, state);

            processor.Start();
            try
            {
                FeedFrames(capture, processor, expectation.Frame - windowFrames, expectation.Frame + windowFrames, frameDelayMilliseconds);

                if (WaitForExpectedMoon(state, expectation.RegionType, expectedMoon, settleMilliseconds))
                {
                    var snapshot = state.CreateSnapshot();
                    Console.WriteLine(
                        $"PASS {expectation.Name}: pending {snapshot.Pending.Count}, " +
                        $"collected {snapshot.Collected.Count}, uncounted {snapshot.UncountedCollected.Count}");
                    return true;
                }

                var finalSnapshot = state.CreateSnapshot();
                Console.WriteLine(
                    $"FAIL {expectation.Name}: pending [{string.Join(", ", finalSnapshot.Pending.Select(m => m.Id))}], " +
                    $"collected [{string.Join(", ", finalSnapshot.Collected.Select(m => m.Id))}], " +
                    $"uncounted [{string.Join(", ", finalSnapshot.UncountedCollected.Select(m => m.Id))}]");
                return false;
            }
            finally
            {
                processor.Stop();
            }
        }

        private static Moon FindExpectedMoon(MoonRepository repo, RuntimeExpectation expectation)
        {
            var settings = new RunSettings();
            var expectedMoon = repo.GetCollectionCandidates(expectation.Kingdom, settings)
                .Concat(repo.GetTalkatooCandidates(expectation.Kingdom, settings))
                .FirstOrDefault(moon => moon.Id == expectation.ExpectedMoonId);

            if (expectedMoon == null)
                throw new InvalidOperationException($"Could not find moon {expectation.ExpectedMoonId} in {expectation.Kingdom}.");

            return expectedMoon;
        }

        private static bool WaitForExpectedMoon(
            GameState state,
            OcrRegionType regionType,
            Moon expectedMoon,
            int settleMilliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(settleMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                if (StateHasExpectedMoon(state, regionType, expectedMoon))
                    return true;

                Thread.Sleep(25);
            }

            return false;
        }

        private static void FeedFrames(
            VideoCapture capture,
            FrameProcessor processor,
            int startFrame,
            int endFrame,
            int frameDelayMilliseconds)
        {
            startFrame = Math.Max(0, startFrame);
            capture.Set(VideoCaptureProperties.PosFrames, startFrame);

            using var frame = new Mat();
            for (var frameIndex = startFrame; frameIndex <= endFrame; frameIndex++)
            {
                if (!capture.Read(frame) || frame.Empty())
                    break;

                processor.PushFrame(new VideoFrame(frame.Clone(), DateTime.UtcNow));
                if (frameDelayMilliseconds > 0)
                    Thread.Sleep(frameDelayMilliseconds);
            }
        }

        private static bool StateHasExpectedMoon(GameState state, OcrRegionType regionType, Moon expectedMoon)
        {
            var snapshot = state.CreateSnapshot();
            return regionType == OcrRegionType.Talkatoo
                ? snapshot.Pending.Any(moon => moon.Id == expectedMoon.Id)
                : snapshot.Collected.Any(moon => moon.Id == expectedMoon.Id);
        }

        private readonly record struct RuntimeExpectation(
            string Name,
            OcrRegionType RegionType,
            string Kingdom,
            int Frame,
            int ExpectedMoonId);
    }
}
