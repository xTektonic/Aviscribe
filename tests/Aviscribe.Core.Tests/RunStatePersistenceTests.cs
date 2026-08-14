using Aviscribe.Core.Ocr;
using System.Text.Json;

namespace Aviscribe.Core.Tests;

public sealed class RunStatePersistenceTests
{
    private readonly MoonRepository _repository = MoonRepository.LoadDefault();

    [Fact]
    public void KingdomSwitchesPreserveIndependentState()
    {
        var state = new GameState();
        var cascade = Candidates("Cascade", state.Settings);
        var sand = Candidates("Sand", state.Settings);

        state.SetKingdom("Cascade");
        state.MoveToPending(cascade[0]);
        state.MoveToCollected(cascade[1]);
        state.MoveToUncounted(cascade[2]);

        state.SetKingdom("Sand");
        state.MoveToPending(sand[0]);
        Assert.Single(state.Pending);
        Assert.Empty(state.Collected);
        Assert.Empty(state.UncountedCollected);

        state.SetKingdom("Cascade");
        Assert.Equal(cascade[0].Id, Assert.Single(state.Pending).Id);
        Assert.Equal(cascade[1].Id, Assert.Single(state.Collected).Id);
        Assert.Equal(cascade[2].Id, Assert.Single(state.UncountedCollected).Id);

        state.SetKingdom("Sand");
        Assert.Equal(sand[0].Id, Assert.Single(state.Pending).Id);
    }

    [Fact]
    public void MultiKingdomStateRoundTripsAndResetRunClearsEverything()
    {
        WithTemporaryStateFile((path, store) =>
        {
            var state = new GameState();
            state.Settings.AutomaticallySwitchKingdoms = true;
            state.Settings.AdaptiveTalkatooDetection = true;
            var cascade = Candidates("Cascade", state.Settings);
            var sand = Candidates("Sand", state.Settings);
            state.SetKingdom("Cascade");
            state.MoveToPending(cascade[0]);
            state.SetKingdom("Sand");
            state.MoveToCollected(sand[0]);

            store.Save(path, state.CreateSnapshot(), true, "pending.txt");
            var saved = Assert.IsType<SavedRunState>(store.Load(path));
            var restored = new GameState();
            store.Restore(restored, saved);

            Assert.Equal("Sand", restored.CurrentKingdom);
            Assert.True(restored.Settings.AutomaticallySwitchKingdoms);
            Assert.True(restored.Settings.AdaptiveTalkatooDetection);
            Assert.Equal(sand[0].Id, Assert.Single(restored.Collected).Id);
            restored.SetKingdom("Cascade");
            Assert.Equal(cascade[0].Id, Assert.Single(restored.Pending).Id);
            restored.SetKingdom("Sand");

            restored.ResetRun();
            Assert.Equal("Sand", restored.CurrentKingdom);
            Assert.Empty(restored.Pending);
            Assert.Empty(restored.Pending);
            Assert.Empty(restored.Collected);
            Assert.Empty(restored.UncountedCollected);
            Assert.All(restored.CreateSnapshot().KingdomStates.Values, kingdom =>
            {
                Assert.Empty(kingdom.Pending);
                Assert.Empty(kingdom.Collected);
                Assert.Empty(kingdom.UncountedCollected);
            });
        });
    }

    [Fact]
    public void EnablingPostgameModePreservesRunState()
    {
        var state = new GameState();
        var cascade = Candidates("Cascade", state.Settings);
        state.SetKingdom("Cascade");
        state.MoveToCollected(cascade[0]);

        var changed = state.SetIncludePostGameKingdoms(true);

        Assert.True(changed);
        Assert.True(state.Settings.IncludePostGameKingdoms);
        Assert.Equal("Cascade", state.CurrentKingdom);
        Assert.Equal(cascade[0].Id, Assert.Single(state.Collected).Id);
    }

    [Fact]
    public void DisablingPostgameModeResetsEveryKingdomAndSelectsCascade()
    {
        var state = new GameState();
        state.SetIncludePostGameKingdoms(true);
        var cascade = Candidates("Cascade", state.Settings);
        var mushroom = Candidates("Mushroom", state.Settings);
        state.SetKingdom("Cascade");
        state.MoveToPending(cascade[0]);
        state.SetKingdom("Mushroom");
        state.MoveToCollected(mushroom[0]);

        var changed = state.SetIncludePostGameKingdoms(false);

        Assert.True(changed);
        Assert.False(state.Settings.IncludePostGameKingdoms);
        Assert.Equal(GameState.InitialKingdom, state.CurrentKingdom);
        Assert.Empty(state.Pending);
        Assert.Empty(state.Collected);
        Assert.Empty(state.UncountedCollected);
        Assert.All(state.CreateSnapshot().KingdomStates.Values, kingdom =>
        {
            Assert.Empty(kingdom.Pending);
            Assert.Empty(kingdom.Collected);
            Assert.Empty(kingdom.UncountedCollected);
        });
    }

    [Theory]
    [InlineData("Cap")]
    [InlineData("Cloud")]
    [InlineData("Ruined")]
    [InlineData("Mushroom")]
    [InlineData("Moon")]
    [InlineData("Dark")]
    [InlineData("Darker")]
    public void DisablingPostgameModeSelectsCascadeForKingdomsHiddenInNormalMode(
        string postgameKingdom)
    {
        var state = new GameState();
        state.SetIncludePostGameKingdoms(true);
        state.SetKingdom(postgameKingdom);

        state.SetIncludePostGameKingdoms(false);

        Assert.Equal(GameState.InitialKingdom, state.CurrentKingdom);
    }

    [Fact]
    public void DisablingPostgameModePreservesSelectedNormalKingdom()
    {
        var state = new GameState();
        state.SetIncludePostGameKingdoms(true);
        var cascade = Candidates("Cascade", state.Settings);
        var sand = Candidates("Sand", state.Settings);
        state.SetKingdom("Cascade");
        state.MoveToPending(cascade[0]);
        state.SetKingdom("Sand");
        state.MoveToCollected(sand[0]);

        var changed = state.SetIncludePostGameKingdoms(false);

        Assert.True(changed);
        Assert.False(state.Settings.IncludePostGameKingdoms);
        Assert.Equal("Sand", state.CurrentKingdom);
        Assert.Empty(state.Pending);
        Assert.Empty(state.Collected);
        Assert.Empty(state.UncountedCollected);
        Assert.All(state.CreateSnapshot().KingdomStates.Values, kingdom =>
        {
            Assert.Empty(kingdom.Pending);
            Assert.Empty(kingdom.Collected);
            Assert.Empty(kingdom.UncountedCollected);
        });
    }

    [Fact]
    public void LegacySingleKingdomStateStillRestores()
    {
        var settings = new RunSettings();
        var moon = Candidates("Cascade", settings)[0];
        var json = $$"""
            {
              "CurrentKingdom": "Cascade",
              "Settings": {},
              "PendingMoonIds": [{{moon.Id}}],
              "CollectedMoonIds": [],
              "UncountedCollectedMoonIds": []
            }
            """;
        var saved = Assert.IsType<SavedRunState>(
            JsonSerializer.Deserialize<SavedRunState>(json));
        var restored = new GameState();

        new RunStateStore(_repository).Restore(restored, saved);

        Assert.Equal("Cascade", restored.CurrentKingdom);
        Assert.Equal(moon.Id, Assert.Single(restored.Pending).Id);
    }

    [Fact]
    public void AmbiguousReviewsRoundTripWithKingdomAndCandidateMetadata()
    {
        WithTemporaryStateFile((path, store) =>
        {
            var state = new GameState();
            state.SetKingdom("Cascade");
            var cascade = Candidates("Cascade", state.Settings);
            var sand = Candidates("Sand", state.Settings);
            var reviews = new[]
            {
                new KingdomAmbiguousReview(
                    "Cascade",
                    new AmbiguousOcrResult(
                        OcrRegionType.Talkatoo,
                        "cascade read",
                        new[] { (cascade[0], 0.91), (cascade[1], 0.83) })),
                new KingdomAmbiguousReview(
                    "Sand",
                    new AmbiguousOcrResult(
                        OcrRegionType.MoonGet,
                        "sand read",
                        new[] { (sand[0], 0.89) }))
            };

            store.Save(
                path,
                state.CreateSnapshot(),
                true,
                "pending.txt",
                ambiguousReviews: reviews);
            var saved = Assert.IsType<SavedRunState>(store.Load(path));
            var restored = store.RestoreReviews(saved);

            Assert.Equal(2, restored.Count);
            var cascadeReview = Assert.Single(restored, review => review.Kingdom == "Cascade");
            Assert.Equal(OcrRegionType.Talkatoo, cascadeReview.Result.Type);
            Assert.Equal("cascade read", cascadeReview.Result.Text);
            Assert.Equal(2, cascadeReview.Result.Candidates.Count);
            Assert.Equal(cascade[0].Kingdom, cascadeReview.Result.Candidates[0].Moon.Kingdom);
            Assert.Equal(cascade[0].Id, cascadeReview.Result.Candidates[0].Moon.Id);
            Assert.Equal(0.91, cascadeReview.Result.Candidates[0].Score, 3);
            Assert.Single(restored, review => review.Kingdom == "Sand");
        });
    }

    private List<Moon> Candidates(string kingdom, RunSettings settings)
    {
        return _repository.GetCollectionCandidates(kingdom, settings);
    }

    private void WithTemporaryStateFile(Action<string, RunStateStore> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AviscribeTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "run-state.json");
        try
        {
            action(path, new RunStateStore(_repository));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
