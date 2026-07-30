using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class CollectionConfirmationSmoke
    {
        public static void Run()
        {
            LongHeldResolvedMoonGetReadsOnce();
            LongHeldResolvedStoryMoonReadsOnce();
            DetectorDropoutsDoNotRearmResolvedOverlay();
            AnimationInstabilityDoesNotBlockStoryMoon();
            ConfirmedDisappearanceRearmsMoonGet();
            UnresolvedMoonGetRetriesOnlyOnce();
            InFlightMoonGetGenerationIsNotDuplicated();
            StoryMoonWinsSuccessfulOverlap();
            MoonGetFallbackFollowsUnresolvedStoryMoon();
            KingdomChangeClearsCollectionConfirmation();
            Console.WriteLine("Collection confirmation smoke passed.");
        }

        private static void LongHeldResolvedMoonGetReadsOnce()
        {
            var moon = CollectionMoon(1, "Cascade", "Long MoonGet");
            var state = EnglishState("Cascade");
            state.AddPending(moon);
            using var innerOcr = new ScriptedOcrService((_, _) => moon.English);
            var attempts = RunHeldOverlay(
                moon,
                state,
                innerOcr,
                new ScriptedDetector((type, _) => type == OcrRegionType.MoonGet),
                frameCount: 70);

            AssertAttempts("long-held MoonGet", attempts, moonGet: 1, storyMoon: 0);
        }

        private static void LongHeldResolvedStoryMoonReadsOnce()
        {
            var moon = CollectionMoon(1, "Cascade", "Long StoryMoon", isStory: true);
            var state = EnglishState("Cascade");
            using var innerOcr = new ScriptedOcrService((_, _) => moon.English);
            var attempts = RunHeldOverlay(
                moon,
                state,
                innerOcr,
                new ScriptedDetector((type, _) => type == OcrRegionType.StoryMoon),
                frameCount: 50);

            AssertAttempts("long-held StoryMoon", attempts, moonGet: 0, storyMoon: 1);
        }

        private static void DetectorDropoutsDoNotRearmResolvedOverlay()
        {
            var moonGet = CollectionMoon(1, "Cascade", "Dropout MoonGet");
            var moonGetState = EnglishState("Cascade");
            moonGetState.AddPending(moonGet);
            using (var innerOcr = new ScriptedOcrService((_, _) => moonGet.English))
            {
                var attempts = RunHeldOverlay(
                    moonGet,
                    moonGetState,
                    innerOcr,
                    new ScriptedDetector((type, call) =>
                        type == OcrRegionType.MoonGet &&
                        call != 2),
                    frameCount: 45);
                AssertAttempts("one-sample MoonGet dropout", attempts, moonGet: 1, storyMoon: 0);
            }

            var storyMoon = CollectionMoon(2, "Cascade", "Dropout StoryMoon", isStory: true);
            var storyState = EnglishState("Cascade");
            using var storyOcr = new ScriptedOcrService((_, _) => storyMoon.English);
            var storyAttempts = RunHeldOverlay(
                storyMoon,
                storyState,
                storyOcr,
                new ScriptedDetector((type, call) =>
                    type == OcrRegionType.StoryMoon &&
                    call is not 4 and not 5),
                frameCount: 30);
            AssertAttempts("multi-sample StoryMoon dropout", storyAttempts, moonGet: 0, storyMoon: 1);
        }

        private static void AnimationInstabilityDoesNotBlockStoryMoon()
        {
            var moon = CollectionMoon(1, "Cascade", "Animated StoryMoon", isStory: true);
            var state = EnglishState("Cascade");
            var repo = Repo(moon);
            using var innerOcr = new ScriptedOcrService((_, _) => moon.English);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var processor = Processor(repo, state, ocr, new ScriptedDetector(
                (type, _) => type == OcrRegionType.StoryMoon));

            processor.Start();
            try
            {
                PushFrames(processor, 30, changingStoryPattern: true);
                WaitForCollected(state, moon);
                Thread.Sleep(200);
                AssertAttempts(
                    "animated StoryMoon",
                    ocr.Snapshot(),
                    moonGet: 0,
                    storyMoon: 1);
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void ConfirmedDisappearanceRearmsMoonGet()
        {
            var first = CollectionMoon(1, "Cascade", "First MoonGet");
            var second = CollectionMoon(2, "Cascade", "Second MoonGet");
            var state = EnglishState("Cascade");
            state.AddPending(first);
            state.AddPending(second);
            var repo = Repo(first, second);
            using var innerOcr = new ScriptedOcrService((_, attempt) =>
                attempt == 1 ? first.English : second.English);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var reappearAt =
                CollectionConfirmationProfile.MoonGet.RequiredAbsentObservations + 2;
            var detector = new ScriptedDetector((type, call) =>
                type == OcrRegionType.MoonGet &&
                (call == 1 || call >= reappearAt));
            var processor = Processor(repo, state, ocr, detector);

            processor.Start();
            try
            {
                PushFrames(
                    processor,
                    (reappearAt + 3) *
                    CollectionConfirmationProfile.MoonGet.DetectionIntervalFrames);
                WaitForCollected(state, first);
                WaitForCollected(state, second);
                Thread.Sleep(200);
                AssertAttempts(
                    "confirmed MoonGet disappearance",
                    ocr.Snapshot(),
                    moonGet: 2,
                    storyMoon: 0);
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void UnresolvedMoonGetRetriesOnlyOnce()
        {
            var moon = CollectionMoon(1, "Cascade", "Unresolved MoonGet");
            var state = EnglishState("Cascade");
            state.AddPending(moon);
            var repo = Repo(moon);
            using var innerOcr = new ScriptedOcrService((_, _) => string.Empty);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var processor = Processor(
                repo,
                state,
                ocr,
                new ScriptedDetector((type, _) => type == OcrRegionType.MoonGet));

            processor.Start();
            try
            {
                PushFrames(processor, 80);
                WaitFor(() => ocr.Snapshot().MoonGetAttempts >= 2, "MoonGet retry did not occur.");
                Thread.Sleep(300);

                var attempts = ocr.Snapshot();
                if (attempts.MoonGetAttempts != 2)
                {
                    throw new InvalidOperationException(
                        $"Unchanged unresolved MoonGet used {attempts.MoonGetAttempts} attempts instead of two.");
                }
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void InFlightMoonGetGenerationIsNotDuplicated()
        {
            var moon = CollectionMoon(1, "Cascade", "Slow MoonGet");
            var state = EnglishState("Cascade");
            state.AddPending(moon);
            var repo = Repo(moon);
            using var innerOcr = new ScriptedOcrService(
                (_, _) => moon.English,
                blockFirstRead: true);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var processor = Processor(
                repo,
                state,
                ocr,
                new ScriptedDetector((type, call) =>
                    type == OcrRegionType.MoonGet &&
                    call != 2));

            processor.Start();
            try
            {
                PushFrames(processor, 8);
                if (!innerOcr.Started.Wait(TimeSpan.FromSeconds(3)))
                    throw new InvalidOperationException("Slow MoonGet OCR did not start.");

                PushFrames(processor, 35);
                innerOcr.Release();
                WaitForCollected(state, moon);
                Thread.Sleep(250);
                AssertAttempts(
                    "in-flight MoonGet generation",
                    ocr.Snapshot(),
                    moonGet: 1,
                    storyMoon: 0);
            }
            finally
            {
                innerOcr.Release();
                processor.Stop();
            }
        }

        private static void StoryMoonWinsSuccessfulOverlap()
        {
            var moon = CollectionMoon(1, "Cascade", "Overlap StoryMoon", isStory: true);
            var state = EnglishState("Cascade");
            var repo = Repo(moon);
            using var innerOcr = new ScriptedOcrService((_, _) => moon.English);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var processor = Processor(repo, state, ocr, new ScriptedDetector(
                (type, _) => type is OcrRegionType.MoonGet or OcrRegionType.StoryMoon));

            processor.Start();
            try
            {
                PushFrames(processor, 35);
                WaitForCollected(state, moon);
                Thread.Sleep(250);
                AssertAttempts(
                    "successful collection overlap",
                    ocr.Snapshot(),
                    moonGet: 0,
                    storyMoon: 1);
                AssertOrder(innerOcr, OcrRegionType.StoryMoon);
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void MoonGetFallbackFollowsUnresolvedStoryMoon()
        {
            var moon = CollectionMoon(1, "Cascade", "Overlap MoonGet");
            var state = EnglishState("Cascade");
            state.AddPending(moon);
            var repo = Repo(moon);
            using var innerOcr = new ScriptedOcrService((type, _) =>
                type == OcrRegionType.StoryMoon
                    ? string.Empty
                    : moon.English);
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var processor = Processor(repo, state, ocr, new ScriptedDetector(
                (type, _) => type is OcrRegionType.MoonGet or OcrRegionType.StoryMoon));

            processor.Start();
            try
            {
                PushFrames(processor, 60);
                WaitForCollected(state, moon);
                Thread.Sleep(250);
                AssertAttempts(
                    "unresolved collection overlap fallback",
                    ocr.Snapshot(),
                    moonGet: 1,
                    storyMoon: 1);
                AssertOrder(
                    innerOcr,
                    OcrRegionType.StoryMoon,
                    OcrRegionType.MoonGet);
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void KingdomChangeClearsCollectionConfirmation()
        {
            var cascadeMoon = CollectionMoon(1, "Cascade", "Kingdom Collection");
            var lakeMoon = CollectionMoon(1, "Lake", "Kingdom Collection");
            var state = EnglishState("Cascade");
            state.AddPending(cascadeMoon);
            var repo = Repo(cascadeMoon, lakeMoon);
            using var innerOcr = new ScriptedOcrService((_, _) => "Kingdom Collection");
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var processor = Processor(
                repo,
                state,
                ocr,
                new ScriptedDetector((type, _) => type == OcrRegionType.MoonGet));

            processor.Start();
            try
            {
                PushFrames(processor, 15);
                WaitForCollected(state, cascadeMoon);

                state.SetKingdom("Lake");
                state.AddPending(lakeMoon);
                PushFrames(processor, 20);
                WaitForCollected(state, lakeMoon);
                Thread.Sleep(200);

                AssertAttempts(
                    "kingdom-reset MoonGet",
                    ocr.Snapshot(),
                    moonGet: 2,
                    storyMoon: 0);
            }
            finally
            {
                processor.Stop();
            }
        }

        private static OcrAttemptSnapshot RunHeldOverlay(
            Moon moon,
            GameState state,
            ScriptedOcrService innerOcr,
            ITextPresenceDetector detector,
            int frameCount)
        {
            var ocr = new OcrAttemptCountingProxy(innerOcr);
            var processor = Processor(Repo(moon), state, ocr, detector);
            processor.Start();
            try
            {
                PushFrames(processor, frameCount);
                WaitForCollected(state, moon);
                Thread.Sleep(250);
                return ocr.Snapshot();
            }
            finally
            {
                processor.Stop();
            }
        }

        private static FrameProcessor Processor(
            MoonRepository repo,
            GameState state,
            IOcrService ocr,
            ITextPresenceDetector detector)
        {
            var matcher = new MoonMatcher(
                repo,
                GameLanguage.English,
                GameLanguage.English);
            return new FrameProcessor(ocr, matcher, state, detector);
        }

        private static Moon CollectionMoon(
            int id,
            string kingdom,
            string english,
            bool isStory = false)
        {
            return new Moon
            {
                Id = id,
                Kingdom = kingdom,
                English = english,
                IsStory = isStory
            };
        }

        private static MoonRepository Repo(params Moon[] moons)
        {
            var repo = new MoonRepository();
            repo.Moons.AddRange(moons);
            return repo;
        }

        private static GameState EnglishState(string kingdom)
        {
            var state = new GameState();
            state.Settings.InputLanguage = GameLanguage.English;
            state.Settings.OutputLanguage = GameLanguage.English;
            state.SetKingdom(kingdom);
            return state;
        }

        private static void PushFrames(
            FrameProcessor processor,
            int count,
            bool changingStoryPattern = false)
        {
            for (var index = 0; index < count; index++)
            {
                using var image = new Mat(
                    new Size(1920, 1080),
                    MatType.CV_8UC3,
                    Scalar.Black);
                if (changingStoryPattern)
                {
                    var left = index % 2 == 0;
                    var pattern = new Rect(
                        left ? 450 : 1000,
                        820,
                        550,
                        150);
                    Cv2.Rectangle(image, pattern, Scalar.White, thickness: -1);
                }

                processor.PushFrame(new VideoFrame(image.Clone(), DateTime.UtcNow));
                Thread.Sleep(12);
            }
        }

        private static void WaitForCollected(GameState state, Moon moon)
        {
            WaitFor(
                () =>
                {
                    var snapshot = state.CreateSnapshot();
                    return snapshot.Collected.Any(item => item.Id == moon.Id) ||
                        snapshot.UncountedCollected.Any(item => item.Id == moon.Id);
                },
                $"{moon.English} did not resolve.");
        }

        private static void WaitFor(Func<bool> condition, string failureMessage)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return;

                Thread.Sleep(20);
            }

            throw new InvalidOperationException(failureMessage);
        }

        private static void AssertAttempts(
            string name,
            OcrAttemptSnapshot attempts,
            int moonGet,
            int storyMoon)
        {
            if (attempts.MoonGetAttempts != moonGet ||
                attempts.StoryMoonAttempts != storyMoon)
            {
                throw new InvalidOperationException(
                    $"{name} used {attempts.MoonGetAttempts} MoonGet and " +
                    $"{attempts.StoryMoonAttempts} StoryMoon attempts; expected " +
                    $"{moonGet} and {storyMoon}.");
            }
        }

        private static void AssertOrder(
            ScriptedOcrService ocr,
            params OcrRegionType[] expected)
        {
            var actual = ocr.AttemptOrder;
            if (!actual.SequenceEqual(expected))
            {
                throw new InvalidOperationException(
                    $"Collection OCR order [{string.Join(", ", actual)}] did not match " +
                    $"[{string.Join(", ", expected)}].");
            }
        }

        private sealed class ScriptedDetector : ITextPresenceDetector
        {
            private readonly Func<OcrRegionType, int, bool> _present;
            private readonly Dictionary<OcrRegionType, int> _calls = new();

            public ScriptedDetector(Func<OcrRegionType, int, bool> present)
            {
                _present = present;
            }

            public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
            {
                var call = _calls.GetValueOrDefault(regionType) + 1;
                _calls[regionType] = call;
                return _present(regionType, call)
                    ? TextPresenceResult.PresentResult(nameof(ScriptedDetector))
                    : TextPresenceResult.Absent(nameof(ScriptedDetector));
            }
        }

        private sealed class ScriptedOcrService : IOcrService, IDisposable
        {
            private readonly Func<OcrRegionType, int, string> _response;
            private readonly ManualResetEventSlim _release = new(false);
            private readonly bool _blockFirstRead;
            private readonly object _lock = new();
            private readonly List<OcrRegionType> _attemptOrder = new();

            public ScriptedOcrService(
                Func<OcrRegionType, int, string> response,
                bool blockFirstRead = false)
            {
                _response = response;
                _blockFirstRead = blockFirstRead;
            }

            public ManualResetEventSlim Started { get; } = new(false);

            public OcrRegionType[] AttemptOrder
            {
                get
                {
                    lock (_lock)
                        return _attemptOrder.ToArray();
                }
            }

            public string ReadText(Mat frame)
            {
                var regionType = (frame.Width, frame.Height) switch
                {
                    (649, 48) => OcrRegionType.Talkatoo,
                    (930, 60) => OcrRegionType.MoonGet,
                    (1100, 150) => OcrRegionType.StoryMoon,
                    _ => throw new InvalidOperationException(
                        $"Unexpected OCR crop {frame.Width}x{frame.Height}.")
                };

                int attempt;
                lock (_lock)
                {
                    _attemptOrder.Add(regionType);
                    attempt = _attemptOrder.Count;
                }

                Started.Set();
                if (_blockFirstRead && attempt == 1)
                    _release.Wait(TimeSpan.FromSeconds(5));

                return _response(regionType, attempt);
            }

            public void Release()
            {
                _release.Set();
            }

            public void Dispose()
            {
                Started.Dispose();
                _release.Dispose();
            }
        }
    }
}
