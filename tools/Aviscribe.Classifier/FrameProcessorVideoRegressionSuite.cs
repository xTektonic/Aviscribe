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

            new("cascade-storymoon-first", OcrRegionType.StoryMoon, "Cascade", 10_782, 1),
            new("sand-moonget-skull-sign", OcrRegionType.MoonGet, "Sand", 32_382, 55),
            new("sand-moonget-palm-notes", OcrRegionType.MoonGet, "Sand", 57_582, 32),
            new("lake-moonget-broken-pillar", OcrRegionType.MoonGet, "Lake", 74_138, 7),
            new("wooded-moonget-fire-cave", OcrRegionType.MoonGet, "Wooded", 84_476, 19),
            new("wooded-moonget-stretching", OcrRegionType.MoonGet, "Wooded", 94_098, 25),
            new("metro-moonget-jump-rope-hero", OcrRegionType.MoonGet, "Metro", 170_217, 29),
        ];

        private static readonly RuntimeNegativeExpectation[] NegativeExpectations =
        [
            new("cascade-background-after-fast-talkatoo", OcrRegionType.Talkatoo, "Cascade", 18_989),
            new("sand-stair-highlight-not-moonget", OcrRegionType.MoonGet, "Sand", 42_393),
            new("snow-pale-railing-not-moonget", OcrRegionType.MoonGet, "Snow", 96_924),
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

            foreach (var expectation in NegativeExpectations)
            {
                if (RunNegativeExpectation(
                    capture,
                    repo,
                    expectation,
                    windowFrames: 30,
                    frameDelayMilliseconds: frameDelayMilliseconds,
                    settleMilliseconds: 500))
                {
                    continue;
                }

                failures.Add(new RuntimeExpectation(
                    expectation.Name,
                    expectation.RegionType,
                    expectation.Kingdom,
                    expectation.Frame,
                    0));
            }

            if (failures.Count > 0)
                throw new InvalidOperationException($"{failures.Count} frame processor video expectation(s) failed.");

            Console.WriteLine(
                $"FrameProcessor video regression passed: {Expectations.Length} expected windows updated state; " +
                $"{NegativeExpectations.Length} negative windows stayed at zero relevant OCR.");
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
            using var innerOcr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage);
            var processor = new FrameProcessor(ocr, matcher, state);
            var failures = new List<RuntimeExpectation>();

            processor.Start();
            try
            {
                foreach (var expectation in Expectations.OrderBy(expectation => expectation.Frame))
                {
                    var attemptsBefore = ocr.Snapshot();
                    var expectedMoon = FindExpectedMoon(repo, expectation);
                    if (!string.Equals(state.CurrentKingdom, expectation.Kingdom, StringComparison.OrdinalIgnoreCase))
                        state.SetKingdom(expectation.Kingdom);

                    if (expectation.RegionType != OcrRegionType.Talkatoo &&
                        !expectedMoon.IsStory &&
                        !state.CreateSnapshot().Pending.Any(moon => moon.Id == expectedMoon.Id))
                    {
                        state.AddPending(expectedMoon);
                    }

                    var leadFrames = windowFrames;
                    if (expectation.RegionType is
                        OcrRegionType.MoonGet or OcrRegionType.StoryMoon)
                    {
                        var profile = CollectionConfirmationProfile.For(
                            expectation.RegionType);
                        leadFrames = Math.Max(
                            windowFrames,
                            profile.RequiredAbsentObservations *
                            profile.DetectionIntervalFrames +
                            windowFrames);
                    }

                    FeedFrames(
                        capture,
                        processor,
                        expectation.Frame - leadFrames,
                        expectation.Frame + windowFrames,
                        frameDelayMilliseconds);

                    var resolved = WaitForExpectedMoon(
                        state,
                        expectation.RegionType,
                        expectedMoon,
                        settleMilliseconds);
                    if (resolved && expectation.RegionType != OcrRegionType.Talkatoo)
                        Thread.Sleep(250);

                    var attempts = ocr.Snapshot() - attemptsBefore;
                    var exactCollectionAttempt = expectation.RegionType == OcrRegionType.Talkatoo ||
                        attempts.TotalCollectionAttempts == 1;
                    if (resolved && exactCollectionAttempt)
                    {
                        var snapshot = state.CreateSnapshot();
                        Console.WriteLine(
                            $"PASS {expectation.Name}: kingdom {snapshot.CurrentKingdom}, " +
                            $"pending {snapshot.Pending.Count}, collected {snapshot.Collected.Count}, " +
                            $"uncounted {snapshot.UncountedCollected.Count}" +
                            CollectionAttemptReport(expectation.RegionType, expectedMoon, snapshot, attempts));
                        continue;
                    }

                    failures.Add(expectation);
                    var finalSnapshot = state.CreateSnapshot();
                    Console.WriteLine(
                        $"FAIL {expectation.Name}: kingdom {finalSnapshot.CurrentKingdom}, " +
                        $"pending [{string.Join(", ", finalSnapshot.Pending.Select(m => m.Id))}], " +
                        $"collected [{string.Join(", ", finalSnapshot.Collected.Select(m => m.Id))}], " +
                        $"uncounted [{string.Join(", ", finalSnapshot.UncountedCollected.Select(m => m.Id))}]" +
                        CollectionAttemptReport(expectation.RegionType, expectedMoon, finalSnapshot, attempts));
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

            using var innerOcr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage);
            var processor = new FrameProcessor(ocr, matcher, state);

            processor.Start();
            try
            {
                FeedFrames(capture, processor, expectation.Frame - windowFrames, expectation.Frame + windowFrames, frameDelayMilliseconds);

                var resolved = WaitForExpectedMoon(
                    state,
                    expectation.RegionType,
                    expectedMoon,
                    settleMilliseconds);
                if (resolved && expectation.RegionType != OcrRegionType.Talkatoo)
                    Thread.Sleep(250);

                var attempts = ocr.Snapshot();
                var exactCollectionAttempt = expectation.RegionType == OcrRegionType.Talkatoo ||
                    attempts.TotalCollectionAttempts == 1;
                if (resolved && exactCollectionAttempt)
                {
                    var snapshot = state.CreateSnapshot();
                    Console.WriteLine(
                        $"PASS {expectation.Name}: pending {snapshot.Pending.Count}, " +
                        $"collected {snapshot.Collected.Count}, uncounted {snapshot.UncountedCollected.Count}" +
                        CollectionAttemptReport(expectation.RegionType, expectedMoon, snapshot, attempts));
                    return true;
                }

                var finalSnapshot = state.CreateSnapshot();
                Console.WriteLine(
                    $"FAIL {expectation.Name}: pending [{string.Join(", ", finalSnapshot.Pending.Select(m => m.Id))}], " +
                    $"collected [{string.Join(", ", finalSnapshot.Collected.Select(m => m.Id))}], " +
                    $"uncounted [{string.Join(", ", finalSnapshot.UncountedCollected.Select(m => m.Id))}]" +
                    CollectionAttemptReport(expectation.RegionType, expectedMoon, finalSnapshot, attempts));
                return false;
            }
            finally
            {
                processor.Stop();
            }
        }

        private static bool RunNegativeExpectation(
            VideoCapture capture,
            MoonRepository repo,
            RuntimeNegativeExpectation expectation,
            int windowFrames,
            int frameDelayMilliseconds,
            int settleMilliseconds)
        {
            var state = new GameState();
            state.SetKingdom(expectation.Kingdom);
            var ocr = new OcrAttemptCountingProxy(new EmptyOcrService());
            var matcher = new MoonMatcher(
                repo,
                state.Settings.InputLanguage);
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
                Thread.Sleep(settleMilliseconds);

                var attempts = ocr.Snapshot();
                var relevantAttempts = expectation.RegionType == OcrRegionType.Talkatoo
                    ? attempts.TalkatooAttempts
                    : attempts.TotalCollectionAttempts;
                Console.WriteLine(
                    $"{(relevantAttempts == 0 ? "PASS" : "FAIL")} {expectation.Name}: " +
                    $"Talkatoo attempts {attempts.TalkatooAttempts}, " +
                    $"MoonGet attempts {attempts.MoonGetAttempts}, " +
                    $"StoryMoon attempts {attempts.StoryMoonAttempts}, " +
                    $"total collection attempts {attempts.TotalCollectionAttempts}");
                return relevantAttempts == 0;
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

        private static string CollectionAttemptReport(
            OcrRegionType regionType,
            Moon expectedMoon,
            GameStateSnapshot snapshot,
            OcrAttemptSnapshot attempts)
        {
            if (regionType == OcrRegionType.Talkatoo)
                return string.Empty;

            var outcome = snapshot.Collected.Any(moon => moon.Id == expectedMoon.Id)
                ? "counted"
                : snapshot.UncountedCollected.Any(moon => moon.Id == expectedMoon.Id)
                    ? "uncounted"
                    : snapshot.Pending.Any(moon => moon.Id == expectedMoon.Id)
                        ? "pending"
                        : "missing";
            var resolved = outcome is "counted" or "uncounted";
            return
                $", MoonGet attempts {attempts.MoonGetAttempts}, " +
                $"StoryMoon attempts {attempts.StoryMoonAttempts}, " +
                $"total collection attempts {attempts.TotalCollectionAttempts}, " +
                $"resolved moon {resolved}, final outcome {outcome}";
        }

        private readonly record struct RuntimeExpectation(
            string Name,
            OcrRegionType RegionType,
            string Kingdom,
            int Frame,
            int ExpectedMoonId);

        private readonly record struct RuntimeNegativeExpectation(
            string Name,
            OcrRegionType RegionType,
            string Kingdom,
            int Frame);

        private sealed class EmptyOcrService : IOcrService
        {
            public string ReadText(Mat frame)
            {
                return string.Empty;
            }
        }
    }
}
