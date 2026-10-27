using System.Security.Cryptography;
using System.Text;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using Aviscribe.Core.Integration;
using Aviscribe.Core.Online;
using SMOVision.Core;
using SMOVision.Core.Capture;
using SMOVision.Vision;

namespace Aviscribe.Core.Tests;

public sealed class SmoIntegrationTests
{
    private static readonly MoonRepository Repository = MoonRepository.LoadDefault();
    private static readonly Guid EvidenceSession = Guid.NewGuid();

    [Fact]
    public void PackagedCatalogPreservesEveryLegacyNameAndOnlineIdentity()
    {
        Assert.Equal(836, Repository.Moons.Count);
        Assert.Equal("E14A68157DC02D844FCA88C79507ACF9AB78AEEEA32CE4CB08EBBB28BDEF5DAF", new OnlineCatalog(Repository).Hash);
        var rows = Repository.Moons.Select(m => string.Join('|', new[] { m.Kingdom, m.Id.ToString() }
            .Concat(Enum.GetValues<GameLanguage>().Select(m.GetName))));
        Assert.Equal("E57139D5A688D6C549A58D954118A7D5B77E4C821859CCF13F557F34BDDB02F4",
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows)))));
        foreach (var id in KingdomId.All) Assert.Equal(id, SmoCatalog.Kingdom(SmoCatalog.KingdomName(id)));
    }

    [Fact]
    public async Task AcceptedHintsAndCollectionsUseRunRulesAndDoNotUndoManualRemoval()
    {
        var state = State("Cascade"); var coordinator = new RunCoordinator(state, Repository);
        await using var session = Session(state, coordinator);
        var moon = Repository.GetTalkatooCandidates("Cascade", state.Settings).First();
        var hint = Occurrence(moon, true);
        session.ApplyUpdate(Update(hint));
        Assert.Single(state.Pending);
        var collection = Occurrence(moon, false);
        session.ApplyUpdate(Update(collection));
        Assert.Single(state.Collected);
        Assert.Empty(state.Pending);
        coordinator.Remove(moon);
        session.ApplyUpdate(Update(collection));
        Assert.Empty(state.Collected);
        Assert.Empty(state.UncountedCollected);
    }

    [Fact]
    public async Task IneligibleGlobalMatchesCannotChangeTheRun()
    {
        var state = State("Cascade"); var coordinator = new RunCoordinator(state, Repository);
        await using var session = Session(state, coordinator);
        session.ApplyUpdate(Update(Occurrence(Repository.GetByKingdom("Sand").First(), true)));
        session.ApplyUpdate(Update(Occurrence(Repository.GetByKingdom("Sand").First(), false)));
        session.ApplyUpdate(Update(Occurrence(Repository.GetByKingdom("Cascade").First(m => m.IsStory), true)));
        Assert.Empty(coordinator.CreateFactSnapshot());
    }

    [Fact]
    public async Task StoryMultiMoonRemainsUncountedInHardcore()
    {
        var state = State("Cascade"); state.Settings.Category = RunCategory.Hardcore;
        var coordinator = new RunCoordinator(state, Repository);
        await using var session = Session(state, coordinator);
        var moon = Repository.GetByKingdom("Cascade").First(m => m.IsMulti);
        session.ApplyUpdate(Update(Occurrence(moon, false)));
        Assert.Equal(3, state.ActualMoonCount); Assert.Equal(0, state.CountedMoonCount);
    }

    [Fact]
    public async Task HintArtUsesCollectionLocationButPreservesHomeIdentity()
    {
        var moon = Repository.Moons.First(m => m.IsHintArt && m.Kingdom == "Sand");
        var state = State(moon.Kingdom); state.Settings.IncludePostGameKingdoms = true;
        var coordinator = new RunCoordinator(state, Repository);
        await using var session = Session(state, coordinator);
        session.ApplyUpdate(Update(Occurrence(moon, true)));
        state.SetKingdom(moon.CollectionLocationKingdom);
        session.ApplyUpdate(Update(Occurrence(moon, false)));
        Assert.Contains(coordinator.CreateFactSnapshot(), f => f.Kingdom == moon.Kingdom && f.MoonId == moon.Id && f.Collected && f.Hinted);
    }

    [Fact]
    public async Task AmbiguousEvidenceProducesBoundedReviewCandidatesWithoutAutoAcceptance()
    {
        var state = State("Sand"); var coordinator = new RunCoordinator(state, Repository);
        await using var session = Session(state, coordinator);
        var matcher = new MoonNameMatcher();
        var text = Repository.GetTalkatooCandidates("Sand", state.Settings).Select(m => m.ChineseTraditional)
            .Select(t => System.Text.RegularExpressions.Regex.Replace(t, "[0-9０-９]+$", ""))
            .First(t => matcher.Match(t, "zh-Hant", KingdomId.Sand).Ambiguous);
        var reviews = new List<Aviscribe.Core.Ocr.AmbiguousOcrResult>();
        session.AmbiguousMatchReceived += (_, result) => reviews.Add(result);
        var detection = Detection.Unavailable(Fields.Talkatoo, ObservationStatus.Unreadable, Evidence() with { RawText = text });
        session.ApplyReview(detection); session.ApplyReview(detection);
        var review = Assert.Single(reviews);
        Assert.InRange(review.Candidates.Count, 1, 3); Assert.Empty(state.Pending);
        session.Invalidate(); session.ApplyReview(detection); Assert.Equal(2, reviews.Count);
    }

    [Fact]
    public async Task OfflineSinkAppliesEveryAcceptedObservationBeforeTheNextFrame()
    {
        var state = State("Cascade"); var coordinator = new RunCoordinator(state, Repository);
        var moon = Repository.GetTalkatooCandidates("Cascade", state.Settings).First();
        var detector = new FakeDetector(context =>
        {
            if (context.Frame.Stamp.Sequence > 1) Assert.Single(state.Pending);
            return ValueTask.FromResult(Detection.Read(Fields.Talkatoo, Occurrence(moon, true), new Evidence(context.Frame.Stamp, Fields.Talkatoo.Id)));
        });
        await using var session = new SmoGameSession(state, Repository, CaptureCropSettings.Default, NullAppDiagnostics.Instance, coordinator) { DetectorFactory = _ => [detector] };
        var source = new PushFrameSource("test", ProcessingMode.Offline, 4);
        for (var i = 0; i < 4; i++) await source.SubmitOwnedAsync(Frame(i), TestContext.Current.CancellationToken);
        source.Complete();
        await session.RunAsync(source, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(AnalysisStatus.Completed, session.Result!.Status);
        Assert.All(session.Result.Detectors.Values, d => Assert.Equal(0, d.Faults));
        Assert.Equal(4, detector.Calls); Assert.Single(state.Pending);
    }

    [Theory]
    [InlineData("reset")]
    [InlineData("crop")]
    [InlineData("kingdom")]
    public async Task ChangingContextRejectsInFlightRecognition(string change)
    {
        var state = State("Cascade"); var coordinator = new RunCoordinator(state, Repository);
        var moon = Repository.GetTalkatooCandidates("Cascade", state.Settings).First();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var detector = new FakeDetector(async context =>
        {
            entered.TrySetResult(); await release.Task;
            return Detection.Read(Fields.Talkatoo, Occurrence(moon, true), new Evidence(context.Frame.Stamp, Fields.Talkatoo.Id));
        });
        await using var session = new SmoGameSession(state, Repository, CaptureCropSettings.Default, NullAppDiagnostics.Instance, coordinator) { DetectorFactory = _ => [detector] };
        var source = new PushFrameSource("test");
        await source.SubmitOwnedAsync(Frame(0), TestContext.Current.CancellationToken); source.Complete();
        var run = session.RunAsync(source, TestContext.Current.CancellationToken);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        switch (change)
        {
            case "reset": coordinator.ResetLocal(); break;
            case "crop": session.UpdateCrop(CaptureCropSettings.Default); break;
            case "kingdom": state.SetKingdom("Sand"); state.SetKingdom("Cascade"); break;
        }
        release.TrySetResult();
        await run.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.All(session.Result!.Detectors.Values, d => Assert.Equal(0, d.Faults));
        Assert.Empty(state.Pending);
    }

    [Fact]
    public async Task SavedCropIsScaledAndAppliedByVisionSession()
    {
        var state = State("Cascade"); var coordinator = new RunCoordinator(state, Repository);
        PixelRegion? seen = null;
        var detector = new FakeDetector(context =>
        {
            seen = context.Region(NormalizedRegion.Full).Bounds;
            return ValueTask.FromResult(Detection.Unavailable(Fields.Talkatoo, ObservationStatus.Absent, new Evidence(context.Frame.Stamp, Fields.Talkatoo.Id)));
        });
        var crop = new CaptureCropSettings { SourceWidth = 64, SourceHeight = 36, X = 16, Y = 9, Width = 32, Height = 18 };
        await using var session = new SmoGameSession(state, Repository, crop, NullAppDiagnostics.Instance, coordinator) { DetectorFactory = _ => [detector] };
        var source = new PushFrameSource("test"); await source.SubmitOwnedAsync(Frame(0), TestContext.Current.CancellationToken); source.Complete();
        await session.RunAsync(source, TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        Assert.Equal(new PixelRegion(16, 9, 32, 18), seen);
    }

    private static GameState State(string kingdom) { var state = new GameState(); state.SetKingdom(kingdom); return state; }
    private static SmoGameSession Session(GameState state, RunCoordinator coordinator) => new(state, Repository, CaptureCropSettings.Default, NullAppDiagnostics.Instance, coordinator);
    private static MoonOccurrence Occurrence(Moon moon, bool hint) => new(Guid.NewGuid(), new MoonId(SmoCatalog.Kingdom(moon.Kingdom), moon.Id), moon.ChineseTraditional, hint);
    private static Evidence Evidence() => new(new FrameStamp("test", EvidenceSession, 0, 1, TimeSpan.Zero), "test");
    private static VisionUpdate Update(MoonOccurrence occurrence)
    {
        var evidence = Evidence();
        return new(1, UpdateKind.ReadingAccepted, "test", evidence, new Reading(occurrence.IsHint ? Fields.Talkatoo : Fields.MoonCollection, occurrence, evidence));
    }
    private static FrameBuffer Frame(int index) => FrameBuffer.Copy(new byte[64 * 36 * 3], 64, 36, 64 * 3, SMOVision.Core.Capture.PixelFormat.Bgr24,
        new FrameStamp("test", Guid.Empty, 0, index, TimeSpan.FromSeconds(index)));
    private sealed class FakeDetector(Func<FrameContext, ValueTask<Detection>> inspect) : Detector<MoonOccurrence>(Fields.Talkatoo, TimeSpan.Zero)
    {
        public int Calls { get; private set; }
        public override ValueTask<Detection> InspectAsync(FrameContext context, CancellationToken cancellationToken) { Calls++; return inspect(context); }
    }
}
