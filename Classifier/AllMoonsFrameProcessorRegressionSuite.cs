using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class AllMoonsFrameProcessorRegressionSuite
    {
        private static readonly RuntimeCollectionExpectation[] CollectionExpectations =
        [
            new("sand-moonget-lone-pillar-standard", OcrRegionType.MoonGet, "Sand", 68_108, 13, RunCategory.Standard, Counted: true),
            new("metro-moonget-rooftop-hop-standard", OcrRegionType.MoonGet, "Metro", 649_105, 25, RunCategory.Standard, Counted: true),
            new("metro-moonget-hidden-scrap-standard", OcrRegionType.MoonGet, "Metro", 650_100, 15, RunCategory.Standard, Counted: true),
            new("luncheon-moonget-volcano-hop-standard", OcrRegionType.MoonGet, "Luncheon", 1_107_280, 35, RunCategory.Standard, Counted: true),
            new("luncheon-moonget-veggies-chest-standard", OcrRegionType.MoonGet, "Luncheon", 1_111_510, 34, RunCategory.Standard, Counted: true),
        ];

        private static readonly RuntimeStoryExpectation[] Expectations =
        [
            new("cascade-story-first-power-moon-standard", "Cascade", 16_317, 1, RunCategory.Standard, Counted: true),
            new("cascade-story-multi-moon-standard", "Cascade", 22_212, 2, RunCategory.Standard, Counted: true),
            new("cascade-story-multi-moon-hardcore", "Cascade", 22_212, 2, RunCategory.Hardcore, Counted: false),
            new("sand-story-atop-highest-tower-standard", "Sand", 76_770, 1, RunCategory.Standard, Counted: true),
            new("sand-story-inverted-pyramid-standard", "Sand", 125_550, 3, RunCategory.Standard, Counted: true),
            new("sand-story-inverted-pyramid-hardcore", "Sand", 125_550, 3, RunCategory.Hardcore, Counted: false),
        ];

        public static void Run(
            string videoPath,
            int windowFrames = 42,
            int frameDelayMilliseconds = 8,
            int settleMilliseconds = 5000)
        {
            using var capture = new VideoCapture(videoPath);
            if (!capture.IsOpened())
                throw new InvalidOperationException($"Could not open video: {videoPath}");

            var repo = MoonRepository.LoadDefault();
            var failures = new List<string>();

            foreach (var expectation in CollectionExpectations)
            {
                if (RunCollectionExpectation(
                    capture,
                    repo,
                    expectation,
                    windowFrames,
                    frameDelayMilliseconds,
                    settleMilliseconds))
                {
                    continue;
                }

                failures.Add(expectation.Name);
            }

            foreach (var expectation in Expectations)
            {
                if (RunExpectation(
                    capture,
                    repo,
                    expectation,
                    windowFrames,
                    frameDelayMilliseconds,
                    settleMilliseconds))
                {
                    continue;
                }

                failures.Add(expectation.Name);
            }

            if (failures.Count > 0)
                throw new InvalidOperationException($"{failures.Count} all-moons FrameProcessor expectation(s) failed.");

            Console.WriteLine(
                $"All-moons FrameProcessor regression passed: " +
                $"{CollectionExpectations.Length} collection windows and {Expectations.Length} story windows updated state.");
        }

        private static bool RunCollectionExpectation(
            VideoCapture capture,
            MoonRepository repo,
            RuntimeCollectionExpectation expectation,
            int windowFrames,
            int frameDelayMilliseconds,
            int settleMilliseconds)
        {
            var expectedMoon = FindExpectedMoon(repo, expectation.Kingdom, expectation.ExpectedMoonId);
            var state = new GameState();
            state.SetKingdom(expectation.Kingdom);
            state.Settings.Category = expectation.Category;
            state.Settings.IncludePostGameKingdoms = true;

            if (expectation.Counted)
                state.AddPending(expectedMoon);

            using var ocr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage, state.Settings.OutputLanguage);
            var processor = new FrameProcessor(ocr, matcher, state);

            processor.Start();
            try
            {
                FeedFrames(
                    capture,
                    processor,
                    expectation.Frame - windowFrames,
                    expectation.Frame + windowFrames,
                    frameDelayMilliseconds);

                if (WaitForExpectedMoon(state, expectedMoon, expectation.Counted, settleMilliseconds))
                {
                    var snapshot = state.CreateSnapshot();
                    Console.WriteLine(
                        $"PASS {expectation.Name}: counted {snapshot.CountedMoonCount}, actual {snapshot.ActualMoonCount}, " +
                        $"pending [{string.Join(", ", snapshot.Pending.Select(moon => moon.Id))}], " +
                        $"collected [{string.Join(", ", snapshot.Collected.Select(moon => moon.Id))}], " +
                        $"uncounted [{string.Join(", ", snapshot.UncountedCollected.Select(moon => moon.Id))}]");
                    return true;
                }

                var finalSnapshot = state.CreateSnapshot();
                Console.WriteLine(
                    $"FAIL {expectation.Name}: counted {finalSnapshot.CountedMoonCount}, actual {finalSnapshot.ActualMoonCount}, " +
                    $"pending [{string.Join(", ", finalSnapshot.Pending.Select(moon => moon.Id))}], " +
                    $"collected [{string.Join(", ", finalSnapshot.Collected.Select(moon => moon.Id))}], " +
                    $"uncounted [{string.Join(", ", finalSnapshot.UncountedCollected.Select(moon => moon.Id))}]");
                return false;
            }
            finally
            {
                processor.Stop();
            }
        }

        private static bool RunExpectation(
            VideoCapture capture,
            MoonRepository repo,
            RuntimeStoryExpectation expectation,
            int windowFrames,
            int frameDelayMilliseconds,
            int settleMilliseconds)
        {
            var expectedMoon = FindExpectedMoon(repo, expectation.Kingdom, expectation.ExpectedMoonId);
            if (!expectedMoon.IsStory)
                throw new InvalidOperationException($"{expectedMoon.English} is not marked as a story moon.");
            var state = new GameState();
            state.SetKingdom(expectation.Kingdom);
            state.Settings.Category = expectation.Category;
            state.Settings.IncludePostGameKingdoms = true;

            using var ocr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage, state.Settings.OutputLanguage);
            var processor = new FrameProcessor(ocr, matcher, state);

            processor.Start();
            try
            {
                FeedFrames(
                    capture,
                    processor,
                    expectation.Frame - windowFrames,
                    expectation.Frame + windowFrames,
                    frameDelayMilliseconds);

                if (WaitForExpectedMoon(state, expectedMoon, expectation.Counted, settleMilliseconds))
                {
                    var snapshot = state.CreateSnapshot();
                    Console.WriteLine(
                        $"PASS {expectation.Name}: counted {snapshot.CountedMoonCount}, actual {snapshot.ActualMoonCount}, " +
                        $"collected [{string.Join(", ", snapshot.Collected.Select(moon => moon.Id))}], " +
                        $"uncounted [{string.Join(", ", snapshot.UncountedCollected.Select(moon => moon.Id))}]");
                    return true;
                }

                var finalSnapshot = state.CreateSnapshot();
                Console.WriteLine(
                    $"FAIL {expectation.Name}: counted {finalSnapshot.CountedMoonCount}, actual {finalSnapshot.ActualMoonCount}, " +
                    $"collected [{string.Join(", ", finalSnapshot.Collected.Select(moon => moon.Id))}], " +
                    $"uncounted [{string.Join(", ", finalSnapshot.UncountedCollected.Select(moon => moon.Id))}]");
                return false;
            }
            finally
            {
                processor.Stop();
            }
        }

        private static Moon FindExpectedMoon(MoonRepository repo, string kingdom, int expectedMoonId)
        {
            var settings = new RunSettings
            {
                IncludePostGameKingdoms = true
            };
            var expectedMoon = repo.GetCollectionCandidates(kingdom, settings)
                .FirstOrDefault(moon => moon.Id == expectedMoonId);

            if (expectedMoon == null)
                throw new InvalidOperationException($"Could not find moon {expectedMoonId} in {kingdom}.");

            return expectedMoon;
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

        private static bool WaitForExpectedMoon(
            GameState state,
            Moon expectedMoon,
            bool counted,
            int settleMilliseconds)
        {
            var deadline = DateTime.UtcNow.AddMilliseconds(settleMilliseconds);
            while (DateTime.UtcNow < deadline)
            {
                var snapshot = state.CreateSnapshot();
                var hasMoon = counted
                    ? snapshot.Collected.Any(moon => moon.Id == expectedMoon.Id)
                    : snapshot.UncountedCollected.Any(moon => moon.Id == expectedMoon.Id);

                var countIsCorrect = counted
                    ? snapshot.CountedMoonCount == expectedMoon.MoonCountValue &&
                      snapshot.ActualMoonCount == expectedMoon.MoonCountValue
                    : snapshot.CountedMoonCount == 0 &&
                      snapshot.ActualMoonCount == expectedMoon.MoonCountValue;

                if (hasMoon && countIsCorrect)
                    return true;

                Thread.Sleep(25);
            }

            return false;
        }

        private readonly record struct RuntimeStoryExpectation(
            string Name,
            string Kingdom,
            int Frame,
            int ExpectedMoonId,
            RunCategory Category,
            bool Counted);

        private readonly record struct RuntimeCollectionExpectation(
            string Name,
            OcrRegionType RegionType,
            string Kingdom,
            int Frame,
            int ExpectedMoonId,
            RunCategory Category,
            bool Counted);
    }
}
