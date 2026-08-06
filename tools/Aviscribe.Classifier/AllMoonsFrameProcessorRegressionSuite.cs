using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class AllMoonsFrameProcessorRegressionSuite
    {
        private static readonly RuntimeTalkatooExpectation[] TalkatooExpectations =
        [
            new("sand-talkatoo-employees-only", "Sand", 218_490, 43),
            new("sand-talkatoo-alcove-ruins", "Sand", 219_830, 6),
            new("sand-talkatoo-bird-wastes", "Sand", 224_020, 22),
            new("sand-talkatoo-flowing-sands", "Sand", 224_178, 8),
            new("sand-talkatoo-rumble-floor", "Sand", 228_358, 52),
            new("sand-talkatoo-captain-toad", "Sand", 228_518, 37),
            new("mushroom-talkatoo-mushroom-art", "Mushroom", 1_299_062, 41),
            new("mushroom-talkatoo-peach-castle-love", "Mushroom", 1_299_224, 16),
            new("metro-talkatoo-jump-rope-genius", "Metro", 652_332, 30),
            new("metro-talkatoo-building-planter", "Metro", 661_446, 21),
            new("metro-talkatoo-sewer-treasure", "Metro", 661_602, 35),
            new("metro-talkatoo-tourist", "Metro", 676_726, 52),
            new("metro-talkatoo-celebrating-streets", "Metro", 681_594, 36),
            new("luncheon-talkatoo-two-flames", "Luncheon", 1_090_804, 31),
            new("luncheon-talkatoo-captain-toad", "Luncheon", 1_099_184, 33),
            new("luncheon-talkatoo-kingdom-art", "Luncheon", 1_099_358, 49),
            new("luncheon-talkatoo-big-pot-swim", "Luncheon", 1_099_530, 36),
            new("luncheon-talkatoo-volcano-hop", "Luncheon", 1_106_554, 35),
            new("luncheon-talkatoo-veggies-chest", "Luncheon", 1_107_868, 34),
            new("luncheon-talkatoo-tourist", "Luncheon", 1_114_498, 48),
        ];

        private static readonly RuntimeTalkatooNegativeExpectation[] TalkatooNegativeExpectations =
        [
            new("sand-yellow-platform-not-talkatoo", 217_178),
            new("sand-yellow-wall-pattern-not-talkatoo", 223_938),
            new("cascade-grass-particles-not-talkatoo", 40_068),
            new("sand-rooftop-yellow-wall-not-talkatoo", 66_282),
            new("sand-stone-platform-not-talkatoo", 76_539),
            new("sand-blue-edge-yellow-trim-not-talkatoo", 80_286),
            new("sand-diagonal-painted-trim-not-talkatoo", 80_295),
            new("sand-bright-paint-patch-not-talkatoo", 80_670),
            new("sand-painted-ornament-not-talkatoo", 80_676),
            new("sand-smooth-painted-surface-not-talkatoo", 80_685),
            new("sand-round-jaxi-object-not-talkatoo", 85_116),
            new("sand-small-round-jaxi-object-not-talkatoo", 85_122),
            new("sand-top-yellow-strip-not-talkatoo", 88_341),
            new("sand-wall-zigzag-not-talkatoo", 88_560),
            new("sand-wall-zigzag-late-not-talkatoo", 88_605),
            new("sand-wall-zigzag-stable-not-talkatoo", 88_611),
        ];

        private static readonly RuntimeCollectionExpectation[] CollectionExpectations =
        [
            new("sand-moonget-lone-pillar-standard", OcrRegionType.MoonGet, "Sand", 68_108, 13, RunCategory.Standard, Counted: true),
            new("metro-moonget-rooftop-hop-standard", OcrRegionType.MoonGet, "Metro", 649_105, 25, RunCategory.Standard, Counted: true),
            new("metro-moonget-hidden-scrap-standard", OcrRegionType.MoonGet, "Metro", 650_100, 15, RunCategory.Standard, Counted: true),
            new("luncheon-moonget-volcano-hop-standard", OcrRegionType.MoonGet, "Luncheon", 1_107_280, 35, RunCategory.Standard, Counted: true),
            new("luncheon-moonget-veggies-chest-standard", OcrRegionType.MoonGet, "Luncheon", 1_111_510, 34, RunCategory.Standard, Counted: true),
        ];

        private static readonly RuntimeCollectionNegativeExpectation[] CollectionNegativeExpectations =
        [
            new("sand-platform-after-lone-pillar-not-moonget", "Sand", 68_624),
            new("snow-map-screen-not-moonget", "Snow", 794_715),
            new("mushroom-note-rail-not-moonget", "Mushroom", 1_206_375),
            new("mushroom-light-platform-not-moonget", "Mushroom", 1_209_415),
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

            foreach (var expectation in TalkatooExpectations)
            {
                if (RunTalkatooExpectation(
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

            foreach (var expectation in CollectionNegativeExpectations)
            {
                if (RunCollectionNegativeExpectation(
                    capture,
                    repo,
                    expectation,
                    windowFrames: 30,
                    frameDelayMilliseconds: frameDelayMilliseconds,
                    settleMilliseconds: 500))
                {
                    continue;
                }

                failures.Add(expectation.Name);
            }

            foreach (var expectation in TalkatooNegativeExpectations)
            {
                if (RunTalkatooNegativeExpectation(
                    capture,
                    repo,
                    expectation,
                    windowFrames: 30,
                    frameDelayMilliseconds: frameDelayMilliseconds,
                    settleMilliseconds: 500))
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
                $"{TalkatooExpectations.Length} Talkatoo windows, " +
                $"{CollectionExpectations.Length} collection windows, and " +
                $"{Expectations.Length} story windows updated state; " +
                $"{CollectionNegativeExpectations.Length} collection negative windows and " +
                $"{TalkatooNegativeExpectations.Length} Talkatoo negative windows stayed at zero OCR.");
        }

        private static bool RunTalkatooExpectation(
            VideoCapture capture,
            MoonRepository repo,
            RuntimeTalkatooExpectation expectation,
            int windowFrames,
            int frameDelayMilliseconds,
            int settleMilliseconds)
        {
            var settings = new RunSettings
            {
                IncludePostGameKingdoms = true
            };
            var expectedMoon = repo
                .GetTalkatooCandidates(expectation.Kingdom, settings)
                .FirstOrDefault(moon => moon.Id == expectation.ExpectedMoonId);
            if (expectedMoon == null)
            {
                throw new InvalidOperationException(
                    $"Could not find Talkatoo moon {expectation.ExpectedMoonId} in {expectation.Kingdom}.");
            }

            var state = new GameState();
            state.SetKingdom(expectation.Kingdom);
            state.Settings.IncludePostGameKingdoms = true;

            using var innerOcr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var ocr = new CountingOcrProxy(innerOcr);
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage);
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

                var deadline = DateTime.UtcNow.AddMilliseconds(settleMilliseconds);
                while (DateTime.UtcNow < deadline)
                {
                    if (state.CreateSnapshot().Pending.Any(moon => moon.Id == expectedMoon.Id))
                        break;

                    Thread.Sleep(25);
                }

                var resolved = state
                    .CreateSnapshot()
                    .Pending
                    .Any(moon => moon.Id == expectedMoon.Id);
                var boundedAttempts = ocr.TalkatooReadCount is >= 1 and <= 2;
                Console.WriteLine(
                    $"{(resolved && boundedAttempts ? "PASS" : "FAIL")} {expectation.Name}: " +
                    $"resolved {resolved}, Talkatoo OCR attempts {ocr.TalkatooReadCount}, " +
                    $"texts [{string.Join(" | ", ocr.TalkatooTexts.Select(text => $"\"{text}\""))}]");
                return resolved && boundedAttempts;
            }
            finally
            {
                processor.Stop();
            }
        }

        private static bool RunTalkatooNegativeExpectation(
            VideoCapture capture,
            MoonRepository repo,
            RuntimeTalkatooNegativeExpectation expectation,
            int windowFrames,
            int frameDelayMilliseconds,
            int settleMilliseconds)
        {
            var state = new GameState();
            state.SetKingdom("Sand");
            var ocr = new EmptyCountingOcrService();
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

                var passed = ocr.TalkatooReadCount == 0;
                Console.WriteLine(
                    $"{(passed ? "PASS" : "FAIL")} {expectation.Name}: " +
                    $"Talkatoo OCR attempts {ocr.TalkatooReadCount}");
                return passed;
            }
            finally
            {
                processor.Stop();
            }
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

            using var innerOcr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage);
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

                var resolved = WaitForExpectedMoon(
                    state,
                    expectedMoon,
                    expectation.Counted,
                    settleMilliseconds);
                if (resolved)
                    Thread.Sleep(250);

                var attempts = ocr.Snapshot();
                if (resolved && attempts.TotalCollectionAttempts == 1)
                {
                    var snapshot = state.CreateSnapshot();
                    Console.WriteLine(
                        $"PASS {expectation.Name}: counted {snapshot.CountedMoonCount}, actual {snapshot.ActualMoonCount}, " +
                        $"pending [{string.Join(", ", snapshot.Pending.Select(moon => moon.Id))}], " +
                        $"collected [{string.Join(", ", snapshot.Collected.Select(moon => moon.Id))}], " +
                        $"uncounted [{string.Join(", ", snapshot.UncountedCollected.Select(moon => moon.Id))}]" +
                        CollectionAttemptReport(expectedMoon, snapshot, attempts));
                    return true;
                }

                var finalSnapshot = state.CreateSnapshot();
                Console.WriteLine(
                    $"FAIL {expectation.Name}: counted {finalSnapshot.CountedMoonCount}, actual {finalSnapshot.ActualMoonCount}, " +
                    $"pending [{string.Join(", ", finalSnapshot.Pending.Select(moon => moon.Id))}], " +
                    $"collected [{string.Join(", ", finalSnapshot.Collected.Select(moon => moon.Id))}], " +
                    $"uncounted [{string.Join(", ", finalSnapshot.UncountedCollected.Select(moon => moon.Id))}]" +
                    CollectionAttemptReport(expectedMoon, finalSnapshot, attempts));
                return false;
            }
            finally
            {
                processor.Stop();
            }
        }

        private static bool RunCollectionNegativeExpectation(
            VideoCapture capture,
            MoonRepository repo,
            RuntimeCollectionNegativeExpectation expectation,
            int windowFrames,
            int frameDelayMilliseconds,
            int settleMilliseconds)
        {
            var state = new GameState();
            state.SetKingdom(expectation.Kingdom);
            state.Settings.IncludePostGameKingdoms = true;
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
                var passed = attempts.TotalCollectionAttempts == 0;
                Console.WriteLine(
                    $"{(passed ? "PASS" : "FAIL")} {expectation.Name}: " +
                    $"MoonGet attempts {attempts.MoonGetAttempts}, " +
                    $"StoryMoon attempts {attempts.StoryMoonAttempts}, " +
                    $"total collection attempts {attempts.TotalCollectionAttempts}");
                return passed;
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

            using var innerOcr = new OnnxOcrService(AppPaths.OcrModelPath, AppPaths.CharsetPath);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage);
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

                var resolved = WaitForExpectedMoon(
                    state,
                    expectedMoon,
                    expectation.Counted,
                    settleMilliseconds);
                if (resolved)
                    Thread.Sleep(250);

                var attempts = ocr.Snapshot();
                if (resolved && attempts.TotalCollectionAttempts == 1)
                {
                    var snapshot = state.CreateSnapshot();
                    Console.WriteLine(
                        $"PASS {expectation.Name}: counted {snapshot.CountedMoonCount}, actual {snapshot.ActualMoonCount}, " +
                        $"collected [{string.Join(", ", snapshot.Collected.Select(moon => moon.Id))}], " +
                        $"uncounted [{string.Join(", ", snapshot.UncountedCollected.Select(moon => moon.Id))}]" +
                        CollectionAttemptReport(expectedMoon, snapshot, attempts));
                    return true;
                }

                var finalSnapshot = state.CreateSnapshot();
                Console.WriteLine(
                    $"FAIL {expectation.Name}: counted {finalSnapshot.CountedMoonCount}, actual {finalSnapshot.ActualMoonCount}, " +
                    $"collected [{string.Join(", ", finalSnapshot.Collected.Select(moon => moon.Id))}], " +
                    $"uncounted [{string.Join(", ", finalSnapshot.UncountedCollected.Select(moon => moon.Id))}]" +
                    CollectionAttemptReport(expectedMoon, finalSnapshot, attempts));
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

        private static string CollectionAttemptReport(
            Moon expectedMoon,
            GameStateSnapshot snapshot,
            OcrAttemptSnapshot attempts)
        {
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

        private readonly record struct RuntimeCollectionNegativeExpectation(
            string Name,
            string Kingdom,
            int Frame);

        private readonly record struct RuntimeTalkatooExpectation(
            string Name,
            string Kingdom,
            int Frame,
            int ExpectedMoonId);

        private readonly record struct RuntimeTalkatooNegativeExpectation(
            string Name,
            int Frame);

        private sealed class CountingOcrProxy : IOcrService
        {
            private readonly IOcrService _inner;

            public CountingOcrProxy(IOcrService inner)
            {
                _inner = inner;
            }

            public int TalkatooReadCount { get; private set; }
            public List<string> TalkatooTexts { get; } = new();

            public string ReadText(Mat frame)
            {
                var text = _inner.ReadText(frame);
                if (frame.Width == 649 && frame.Height == 48)
                {
                    TalkatooReadCount++;
                    TalkatooTexts.Add(text);
                }

                return text;
            }
        }

        private sealed class EmptyCountingOcrService : IOcrService
        {
            public int TalkatooReadCount { get; private set; }

            public string ReadText(Mat frame)
            {
                if (frame.Width == 649 && frame.Height == 48)
                    TalkatooReadCount++;

                return string.Empty;
            }
        }

        private sealed class EmptyOcrService : IOcrService
        {
            public string ReadText(Mat frame)
            {
                return string.Empty;
            }
        }
    }
}
