namespace Aviscribe.Core.Tests;

public sealed class HintArtHandlingTests
{
    private readonly MoonRepository _repository = MoonRepository.LoadDefault();

    [Fact]
    public void HintArtUsesOwningKingdomForTalkatooAndCollectionKingdomForPickup()
    {
        var settings = PostgameSettings();
        var hintArt = CapHintArt(settings);

        Assert.Contains(
            _repository.GetTalkatooCandidates("Cap", settings),
            moon => SameMoon(moon, hintArt));
        Assert.DoesNotContain(
            _repository.GetTalkatooCandidates("Moon", settings),
            moon => SameMoon(moon, hintArt));
        Assert.DoesNotContain(
            _repository.GetCollectionCandidates("Cap", settings),
            moon => SameMoon(moon, hintArt));
        Assert.Contains(
            _repository.GetCollectionCandidates("Moon", settings),
            moon => SameMoon(moon, hintArt));
        Assert.Contains(
            _repository.GetKingdomDisplayCandidates("Cap", settings),
            moon => SameMoon(moon, hintArt));
        Assert.Contains(
            _repository.GetKingdomDisplayCandidates("Moon", settings),
            moon => SameMoon(moon, hintArt));
    }

    [Fact]
    public void TalkatooMirrorsHintArtPendingAndCollectionCountsItOnlyForOwner()
    {
        var state = CreatePostgameState();
        var hintArt = CapHintArt(state.Settings);
        state.SetKingdom("Cap");
        var changedEvents = 0;
        state.Changed += (_, _) => changedEvents++;

        Assert.True(state.TryAddPending(hintArt));

        Assert.Equal(1, changedEvents);
        AssertMoonPending(state, "Cap", hintArt);
        AssertMoonPending(state, "Moon", hintArt);

        state.SetKingdom("Moon");
        changedEvents = 0;
        var outcome = state.MarkCollected(hintArt);

        Assert.Equal(CollectionOutcome.Counted, outcome);
        Assert.Equal(1, changedEvents);
        Assert.Empty(state.Pending);
        Assert.Empty(state.Collected);
        Assert.Empty(state.UncountedCollected);

        var snapshot = state.CreateSnapshot();
        Assert.DoesNotContain(hintArt, snapshot.KingdomStates["Cap"].Pending);
        Assert.DoesNotContain(hintArt, snapshot.KingdomStates["Moon"].Pending);
        Assert.Contains(hintArt, snapshot.KingdomStates["Cap"].Collected);
        Assert.Empty(snapshot.KingdomStates["Moon"].Collected);

        state.SetKingdom("Cap");
        Assert.Contains(hintArt, state.Collected);
        Assert.Empty(state.UncountedCollected);
        Assert.Equal(1, state.CountedMoonCount);
    }

    [Fact]
    public void UnhintedCollectionIsWrongOnlyInOwningKingdom()
    {
        var state = CreatePostgameState();
        var hintArt = CapHintArt(state.Settings);
        state.SetKingdom("Moon");

        var outcome = state.MarkCollected(hintArt);

        Assert.Equal(CollectionOutcome.Uncounted, outcome);
        Assert.Empty(state.Pending);
        Assert.Empty(state.Collected);
        Assert.Empty(state.UncountedCollected);

        state.SetKingdom("Cap");
        Assert.Empty(state.Pending);
        Assert.Empty(state.Collected);
        Assert.Contains(hintArt, state.UncountedCollected);
    }

    [Theory]
    [InlineData("Cap")]
    [InlineData("Moon")]
    public void ResettingEitherKingdomRemovesBothPendingCopies(string kingdom)
    {
        var state = CreatePostgameState();
        var hintArt = CapHintArt(state.Settings);
        state.TryAddPending(hintArt);
        state.SetKingdom(kingdom);

        state.ResetKingdom();

        AssertMoonNotPending(state, "Cap", hintArt);
        AssertMoonNotPending(state, "Moon", hintArt);
    }

    [Fact]
    public void RestoreNormalizesOneSidedHintArtPendingState()
    {
        var state = CreatePostgameState();
        var hintArt = CapHintArt(state.Settings);

        state.RestoreRun(
            "Cap",
            state.Settings,
            new Dictionary<string, KingdomStateSnapshot>
            {
                ["Cap"] = new KingdomStateSnapshot(
                    new[] { hintArt },
                    Array.Empty<Moon>(),
                    Array.Empty<Moon>())
            });

        AssertMoonPending(state, "Cap", hintArt);
        AssertMoonPending(state, "Moon", hintArt);
    }

    [Fact]
    public void ManualMovesAndRemovalCleanBothPendingCopies()
    {
        var state = CreatePostgameState();
        var hintArt = CapHintArt(state.Settings);
        state.SetKingdom("Moon");
        state.MoveToPending(hintArt);

        state.MoveToUncounted(hintArt);

        AssertMoonNotPending(state, "Cap", hintArt);
        AssertMoonNotPending(state, "Moon", hintArt);
        state.SetKingdom("Cap");
        Assert.Contains(state.UncountedCollected, moon => SameMoon(moon, hintArt));

        state.MoveToPending(hintArt);
        Assert.Empty(state.UncountedCollected);
        AssertMoonPending(state, "Cap", hintArt);
        AssertMoonPending(state, "Moon", hintArt);

        Assert.True(state.Remove(hintArt));
        AssertMoonNotPending(state, "Cap", hintArt);
        AssertMoonNotPending(state, "Moon", hintArt);
    }

    [Fact]
    public void MirroredHintArtPendingRoundTripsThroughRunStateStore()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AviscribeTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "run-state.json");
        try
        {
            var state = CreatePostgameState();
            var hintArt = CapHintArt(state.Settings);
            state.SetKingdom("Cap");
            state.TryAddPending(hintArt);
            var store = new RunStateStore(_repository);
            store.Save(path, state.CreateSnapshot(), false, "pending.txt");
            var saved = Assert.IsType<SavedRunState>(store.Load(path));
            var restored = new GameState();

            store.Restore(restored, saved);

            AssertMoonPending(restored, "Cap", hintArt);
            AssertMoonPending(restored, "Moon", hintArt);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MatchingCollectionKingdomDoesNotCreateHintArtBehavior()
    {
        var moon = new Moon
        {
            Id = 1,
            Kingdom = "Cascade",
            CollectionKingdom = "cascade"
        };
        var state = new GameState();
        state.SetKingdom("Cascade");

        Assert.False(moon.IsHintArt);
        Assert.True(state.TryAddPending(moon));
        Assert.Single(state.CreateSnapshot().KingdomStates["Cascade"].Pending);
    }

    private static RunSettings PostgameSettings() => new()
    {
        IncludePostGameKingdoms = true
    };

    private static GameState CreatePostgameState()
    {
        var state = new GameState();
        state.Settings.IncludePostGameKingdoms = true;
        return state;
    }

    private Moon CapHintArt(RunSettings settings)
    {
        return Assert.Single(
            _repository.GetTalkatooCandidates("Cap", settings),
            moon => moon.Id == 17);
    }

    private static void AssertMoonPending(GameState state, string kingdom, Moon moon)
    {
        state.SetKingdom(kingdom);
        Assert.Contains(state.Pending, candidate => SameMoon(candidate, moon));
    }

    private static void AssertMoonNotPending(GameState state, string kingdom, Moon moon)
    {
        state.SetKingdom(kingdom);
        Assert.DoesNotContain(state.Pending, candidate => SameMoon(candidate, moon));
    }

    private static bool SameMoon(Moon left, Moon right)
    {
        return left.Id == right.Id &&
            left.Kingdom.Equals(right.Kingdom, StringComparison.OrdinalIgnoreCase);
    }
}
