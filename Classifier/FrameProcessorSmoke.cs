using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;
using OpenCvSharp;

namespace Aviscribe.Classifier
{
    internal static class FrameProcessorSmoke
    {
        public static void Run()
        {
            StaleOcrDoesNotMutateNewKingdom();
            AmbiguousTalkatooUsesOnlyUnseenCandidate();
            AmbiguousMoonGetUsesPendingCandidate();
            AmbiguousTalkatooStillAsksWhenUnresolved();
            TalkatooBacklogKeepsRapidDistinctReads();
            TalkatooRefreshesChangedTextWithoutAbsentGap();
            FalseTalkatooEnqueuesDoNotCrowdOutLatestRealRead();
            MoonGetSurvivesOverlappingStoryMoon();
            Console.WriteLine("FrameProcessor smoke passed.");
        }

        private static void StaleOcrDoesNotMutateNewKingdom()
        {
            var repo = MoonRepository.LoadDefault();
            var state = new GameState();
            state.SetKingdom("Sand");

            using var ocr = new BlockingOcrService("圓沙丘的頂端");
            var matcher = new MoonMatcher(repo, state.Settings.InputLanguage, state.Settings.OutputLanguage);
            var processor = new FrameProcessor(ocr, matcher, state, new TalkatooOnlyDetector());

            processor.Start();
            try
            {
                PushBlankFrame(processor);
                Thread.Sleep(30);
                PushBlankFrame(processor);

                if (!ocr.Started.Wait(TimeSpan.FromSeconds(3)))
                    throw new InvalidOperationException("OCR did not start during stale kingdom smoke test.");

                state.SetKingdom("Lake");
                ocr.Release();
                Thread.Sleep(300);

                var snapshot = state.CreateSnapshot();
                if (snapshot.Pending.Count != 0 ||
                    snapshot.Collected.Count != 0 ||
                    snapshot.UncountedCollected.Count != 0)
                {
                    throw new InvalidOperationException("Stale OCR changed state after kingdom switch.");
                }
            }
            finally
            {
                ocr.Release();
                processor.Stop();
            }
        }

        private static void PushBlankFrame(FrameProcessor processor)
        {
            using var image = new Mat(new Size(1920, 1080), MatType.CV_8UC3, Scalar.Black);
            processor.PushFrame(new VideoFrame(image.Clone(), DateTime.UtcNow));
        }

        private static void PushPatternFrame(FrameProcessor processor, byte marker, int pattern)
        {
            using var image = new Mat(new Size(1920, 1080), MatType.CV_8UC3, Scalar.Black);
            var bounds = new Rect(666, 862, 649, 48);
            using var crop = new Mat(image, bounds);

            for (var y = 0; y < crop.Height; y++)
            {
                for (var x = 0; x < crop.Width; x++)
                {
                    var bright = pattern switch
                    {
                        0 => x < crop.Width / 2,
                        1 => x >= crop.Width / 2,
                        2 => (x / 12) % 2 == 0,
                        _ => (x / 12) % 2 != 0
                    };
                    var value = bright ? (byte)255 : (byte)0;
                    crop.Set(y, x, new Vec3b(value, value, value));
                }
            }

            crop.Set(0, 0, new Vec3b(marker, marker, marker));
            processor.PushFrame(new VideoFrame(image.Clone(), DateTime.UtcNow));
        }

        private static void AmbiguousTalkatooUsesOnlyUnseenCandidate()
        {
            var moon1 = new Moon { Id = 1, Kingdom = "Cascade", English = "Cascade Timer Challenge 1" };
            var moon2 = new Moon { Id = 2, Kingdom = "Cascade", English = "Cascade Timer Challenge 2" };
            var repo = CreateRepo(moon1, moon2);
            var state = CreateEnglishState("Cascade");
            state.AddPending(moon1);
            state.MarkCollected(moon1);

            using var ocr = new StaticOcrService("Cascade Timer Challenge");
            var matcher = new MoonMatcher(repo, GameLanguage.English, GameLanguage.English);
            var processor = new FrameProcessor(ocr, matcher, state, new RegionOnlyDetector(OcrRegionType.Talkatoo));
            var ambiguousEvents = 0;
            processor.AmbiguousMatchReceived += (_, _) => ambiguousEvents++;

            processor.Start();
            try
            {
                PushBlankFrame(processor);
                Thread.Sleep(30);
                PushBlankFrame(processor);

                WaitFor(() => state.CreateSnapshot().Pending.Any(moon => moon.Id == moon2.Id), "Ambiguous Talkatoo did not resolve to the only unseen candidate.");
                if (ambiguousEvents != 0)
                    throw new InvalidOperationException("Resolved ambiguous Talkatoo read still raised a review event.");
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void AmbiguousMoonGetUsesPendingCandidate()
        {
            var moon1 = new Moon { Id = 1, Kingdom = "Cascade", English = "Cascade Timer Challenge 1" };
            var moon2 = new Moon { Id = 2, Kingdom = "Cascade", English = "Cascade Timer Challenge 2" };
            var repo = CreateRepo(moon1, moon2);
            var state = CreateEnglishState("Cascade");
            state.AddPending(moon2);

            using var ocr = new StaticOcrService("Cascade Timer Challenge");
            var matcher = new MoonMatcher(repo, GameLanguage.English, GameLanguage.English);
            var processor = new FrameProcessor(ocr, matcher, state, new RegionOnlyDetector(OcrRegionType.MoonGet));
            var ambiguousEvents = 0;
            processor.AmbiguousMatchReceived += (_, _) => ambiguousEvents++;

            processor.Start();
            try
            {
                PushBlankFrame(processor);

                WaitFor(() => state.CreateSnapshot().Collected.Any(moon => moon.Id == moon2.Id), "Ambiguous MoonGet did not resolve to the pending candidate.");
                if (ambiguousEvents != 0)
                    throw new InvalidOperationException("Resolved ambiguous MoonGet read still raised a review event.");
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void AmbiguousTalkatooStillAsksWhenUnresolved()
        {
            var moon1 = new Moon { Id = 1, Kingdom = "Cascade", English = "Cascade Timer Challenge 1" };
            var moon2 = new Moon { Id = 2, Kingdom = "Cascade", English = "Cascade Timer Challenge 2" };
            var repo = CreateRepo(moon1, moon2);
            var state = CreateEnglishState("Cascade");

            using var ocr = new StaticOcrService("Cascade Timer Challenge");
            var matcher = new MoonMatcher(repo, GameLanguage.English, GameLanguage.English);
            var processor = new FrameProcessor(ocr, matcher, state, new RegionOnlyDetector(OcrRegionType.Talkatoo));
            var ambiguousEvents = 0;
            processor.AmbiguousMatchReceived += (_, _) => ambiguousEvents++;

            processor.Start();
            try
            {
                PushBlankFrame(processor);
                Thread.Sleep(30);
                PushBlankFrame(processor);

                WaitFor(() => ambiguousEvents > 0, "Unresolved ambiguous Talkatoo read did not raise a review event.");
                var snapshot = state.CreateSnapshot();
                if (snapshot.Pending.Count != 0)
                    throw new InvalidOperationException("Unresolved ambiguous Talkatoo read changed pending state.");
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void TalkatooBacklogKeepsRapidDistinctReads()
        {
            var oldMoon = new Moon { Id = 1, Kingdom = "Cascade", English = "Old Real Moon" };
            var middleMoon = new Moon { Id = 2, Kingdom = "Cascade", English = "Middle Real Moon" };
            var recentMoon = new Moon { Id = 3, Kingdom = "Cascade", English = "Recent Real Moon" };
            var latestMoon = new Moon { Id = 4, Kingdom = "Cascade", English = "Latest Real Moon" };
            var repo = CreateRepo(oldMoon, middleMoon, recentMoon, latestMoon);
            var state = CreateEnglishState("Cascade");
            using var ocr = new ColorOcrService();
            var matcher = new MoonMatcher(repo, GameLanguage.English, GameLanguage.English);
            var processor = new FrameProcessor(ocr, matcher, state, new ColorTalkatooDetector());

            processor.Start();
            try
            {
                PushStablePatternEvent(processor, 30, 0);

                if (!ocr.Started.Wait(TimeSpan.FromSeconds(3)))
                    throw new InvalidOperationException("OCR did not start during bounded queue smoke test.");

                PushStablePatternEvent(processor, 90, 1);
                PushStablePatternEvent(processor, 150, 2);
                PushStablePatternEvent(processor, 210, 3);

                ocr.Release();
                WaitFor(() => state.CreateSnapshot().Pending.Any(moon => moon.Id == latestMoon.Id), "Bounded queue did not process the latest Talkatoo work.");
                Thread.Sleep(300);

                var snapshot = state.CreateSnapshot();
                var pendingIds = snapshot.Pending.Select(moon => moon.Id).OrderBy(id => id).ToArray();
                if (!pendingIds.SequenceEqual(new[] { 1, 2, 3, 4 }))
                    throw new InvalidOperationException($"Rapid Talkatoo backlog lost one or more reads: [{string.Join(", ", pendingIds)}].");
            }
            finally
            {
                ocr.Release();
                processor.Stop();
            }
        }

        private static void TalkatooRefreshesChangedTextWithoutAbsentGap()
        {
            var firstMoon = new Moon { Id = 1, Kingdom = "Cascade", English = "Old Real Moon" };
            var secondMoon = new Moon { Id = 2, Kingdom = "Cascade", English = "Latest Real Moon" };
            var repo = CreateRepo(firstMoon, secondMoon);
            var state = CreateEnglishState("Cascade");
            using var ocr = new ColorOcrService(blockFirstRead: false);
            var matcher = new MoonMatcher(repo, GameLanguage.English, GameLanguage.English);
            var processor = new FrameProcessor(ocr, matcher, state, new ColorTalkatooDetector());

            processor.Start();
            try
            {
                PushPatternFrame(processor, 30, 0);
                WaitFor(() => state.CreateSnapshot().Pending.Any(moon => moon.Id == firstMoon.Id), "Talkatoo did not enqueue the first active-region read.");
                PushPatternFrame(processor, 210, 3);

                WaitFor(() => state.CreateSnapshot().Pending.Any(moon => moon.Id == secondMoon.Id), "Talkatoo did not enqueue changed text while the region stayed active.");
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void FalseTalkatooEnqueuesDoNotCrowdOutLatestRealRead()
        {
            var realMoon = new Moon { Id = 1, Kingdom = "Cascade", English = "Latest Real Moon" };
            var repo = CreateRepo(realMoon);
            var state = CreateEnglishState("Cascade");
            using var ocr = new FalseThenRealColorOcrService();
            var matcher = new MoonMatcher(repo, GameLanguage.English, GameLanguage.English);
            var processor = new FrameProcessor(ocr, matcher, state, new ColorTalkatooDetector());
            var ambiguousEvents = 0;
            processor.AmbiguousMatchReceived += (_, _) => ambiguousEvents++;

            processor.Start();
            try
            {
                PushStablePatternEvent(processor, 30, 0);

                if (!ocr.Started.Wait(TimeSpan.FromSeconds(3)))
                    throw new InvalidOperationException("OCR did not start during false-enqueue backpressure smoke test.");

                PushStablePatternEvent(processor, 90, 1);
                PushStablePatternEvent(processor, 150, 2);
                PushStablePatternEvent(processor, 210, 3);

                ocr.Release();
                WaitFor(() => state.CreateSnapshot().Pending.Any(moon => moon.Id == realMoon.Id), "False Talkatoo enqueues crowded out the latest real read.");
                Thread.Sleep(300);

                var snapshot = state.CreateSnapshot();
                if (snapshot.Pending.Count != 1)
                    throw new InvalidOperationException($"Expected only the latest real Talkatoo read to update pending, got {snapshot.Pending.Count} pending moons.");

                if (ambiguousEvents != 0)
                    throw new InvalidOperationException("False Talkatoo enqueue smoke raised an unexpected review event.");
            }
            finally
            {
                ocr.Release();
                processor.Stop();
            }
        }

        private static void MoonGetSurvivesOverlappingStoryMoon()
        {
            var moon = new Moon { Id = 2, Kingdom = "Cascade", English = "Cascade Timer Challenge 2" };
            var repo = CreateRepo(moon);
            var state = CreateEnglishState("Cascade");
            state.AddPending(moon);
            using var ocr = new CountingOcrService("Cascade Timer Challenge 2");
            var matcher = new MoonMatcher(repo, GameLanguage.English, GameLanguage.English);
            var processor = new FrameProcessor(ocr, matcher, state, new MoonGetAndStoryDetector());

            processor.Start();
            try
            {
                PushBlankFrame(processor);
                Thread.Sleep(30);
                PushBlankFrame(processor);

                WaitFor(() => state.CreateSnapshot().Collected.Any(collected => collected.Id == moon.Id), "MoonGet did not collect while StoryMoon also detected.");
                Thread.Sleep(250);

                if (ocr.ReadCount < 1 || ocr.ReadCount > 2)
                    throw new InvalidOperationException($"Expected one or two OCR reads when MoonGet overlaps StoryMoon, got {ocr.ReadCount}.");
            }
            finally
            {
                processor.Stop();
            }
        }

        private static void PushStablePatternEvent(FrameProcessor processor, byte marker, int pattern)
        {
            PushBlankFrame(processor);
            Thread.Sleep(30);
            PushPatternFrame(processor, marker, pattern);
            Thread.Sleep(30);
            PushPatternFrame(processor, marker, pattern);
            Thread.Sleep(30);
        }

        private static MoonRepository CreateRepo(params Moon[] moons)
        {
            var repo = new MoonRepository();
            repo.Moons.AddRange(moons);
            return repo;
        }

        private static GameState CreateEnglishState(string kingdom)
        {
            var state = new GameState();
            state.Settings.InputLanguage = GameLanguage.English;
            state.Settings.OutputLanguage = GameLanguage.English;
            state.SetKingdom(kingdom);
            return state;
        }

        private static void WaitFor(Func<bool> condition, string failureMessage)
        {
            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                if (condition())
                    return;

                Thread.Sleep(25);
            }

            throw new InvalidOperationException(failureMessage);
        }

        private sealed class BlockingOcrService : IOcrService, IDisposable
        {
            private readonly ManualResetEventSlim _release = new(false);
            private readonly string _text;

            public BlockingOcrService(string text)
            {
                _text = text;
            }

            public ManualResetEventSlim Started { get; } = new(false);

            public string ReadText(Mat frame)
            {
                Started.Set();
                _release.Wait(TimeSpan.FromSeconds(5));
                return _text;
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

        private sealed class ColorOcrService : IOcrService, IDisposable
        {
            private readonly ManualResetEventSlim _release = new(false);
            private readonly bool _blockFirstRead;
            private bool _firstRead = true;

            public ColorOcrService(bool blockFirstRead = true)
            {
                _blockFirstRead = blockFirstRead;
            }

            public ManualResetEventSlim Started { get; } = new(false);

            public string ReadText(Mat frame)
            {
                if (_firstRead)
                {
                    _firstRead = false;
                    Started.Set();
                    if (_blockFirstRead)
                        _release.Wait(TimeSpan.FromSeconds(5));
                }

                var pixel = frame.At<Vec3b>(0, 0);
                return pixel.Item0 switch
                {
                    < 60 => "Old Real Moon",
                    < 120 => "Middle Real Moon",
                    < 180 => "Recent Real Moon",
                    _ => "Latest Real Moon"
                };
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

        private sealed class FalseThenRealColorOcrService : IOcrService, IDisposable
        {
            private readonly ManualResetEventSlim _release = new(false);
            private bool _firstRead = true;

            public ManualResetEventSlim Started { get; } = new(false);

            public string ReadText(Mat frame)
            {
                if (_firstRead)
                {
                    _firstRead = false;
                    Started.Set();
                    _release.Wait(TimeSpan.FromSeconds(5));
                }

                var pixel = frame.At<Vec3b>(0, 0);
                return pixel.Item0 switch
                {
                    < 200 => "no useful moon text here",
                    _ => "Latest Real Moon"
                };
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

        private sealed class TalkatooOnlyDetector : ITextPresenceDetector
        {
            public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
            {
                return regionType == OcrRegionType.Talkatoo
                    ? TextPresenceResult.PresentResult(nameof(TalkatooOnlyDetector))
                    : TextPresenceResult.Absent(nameof(TalkatooOnlyDetector));
            }
        }

        private sealed class RegionOnlyDetector : ITextPresenceDetector
        {
            private readonly OcrRegionType _presentRegionType;

            public RegionOnlyDetector(OcrRegionType presentRegionType)
            {
                _presentRegionType = presentRegionType;
            }

            public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
            {
                return regionType == _presentRegionType
                    ? TextPresenceResult.PresentResult(nameof(RegionOnlyDetector))
                    : TextPresenceResult.Absent(nameof(RegionOnlyDetector));
            }
        }

        private sealed class ColorTalkatooDetector : ITextPresenceDetector
        {
            public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
            {
                if (regionType != OcrRegionType.Talkatoo)
                    return TextPresenceResult.Absent(nameof(ColorTalkatooDetector));

                var pixel = image.At<Vec3b>(0, 0);
                return pixel.Item0 > 0
                    ? TextPresenceResult.PresentResult(nameof(ColorTalkatooDetector))
                    : TextPresenceResult.Absent(nameof(ColorTalkatooDetector));
            }
        }

        private sealed class StaticOcrService : IOcrService, IDisposable
        {
            private readonly string _text;

            public StaticOcrService(string text)
            {
                _text = text;
            }

            public string ReadText(Mat frame)
            {
                return _text;
            }

            public void Dispose()
            {
            }
        }

        private sealed class CountingOcrService : IOcrService, IDisposable
        {
            private readonly string _text;

            public CountingOcrService(string text)
            {
                _text = text;
            }

            public int ReadCount { get; private set; }

            public string ReadText(Mat frame)
            {
                ReadCount++;
                return _text;
            }

            public void Dispose()
            {
            }
        }

        private sealed class MoonGetAndStoryDetector : ITextPresenceDetector
        {
            public TextPresenceResult Detect(OcrRegionType regionType, Mat image)
            {
                return regionType is OcrRegionType.MoonGet or OcrRegionType.StoryMoon
                    ? TextPresenceResult.PresentResult(nameof(MoonGetAndStoryDetector))
                    : TextPresenceResult.Absent(nameof(MoonGetAndStoryDetector));
            }
        }
    }
}
