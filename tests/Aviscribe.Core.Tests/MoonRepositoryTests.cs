namespace Aviscribe.Core.Tests;

public sealed class MoonRepositoryTests
{
    private readonly MoonRepository _repository = MoonRepository.LoadDefault();

    [Fact]
    public void TalkatooCandidatesIncludeOnlyMushroomStoryMultiMoons()
    {
        var settings = new RunSettings { IncludePostGameKingdoms = true };

        var candidates = _repository.GetTalkatooCandidates("Mushroom", settings);
        var storyCandidates = candidates.Where(moon => moon.IsStory).ToList();

        Assert.Equal(Enumerable.Range(33, 6), storyCandidates.Select(moon => moon.Id));
        Assert.All(storyCandidates, moon => Assert.True(moon.IsMulti));
        Assert.Contains(candidates, moon => !moon.IsStory);
    }

    [Fact]
    public void TalkatooCandidatesExcludeStoryMoonsOutsideMushroom()
    {
        var settings = new RunSettings { IncludePostGameKingdoms = true };

        var candidates = _repository.GetTalkatooCandidates("Cascade", settings);

        Assert.NotEmpty(candidates);
        Assert.DoesNotContain(candidates, moon => moon.IsStory);
    }

    [Fact]
    public void MushroomTalkatooCandidatesRespectPostgameSetting()
    {
        var settings = new RunSettings { IncludePostGameKingdoms = false };

        Assert.Empty(_repository.GetTalkatooCandidates("Mushroom", settings));
    }

    [Fact]
    public void MushroomRematchMultiCollectsAsStoryMoonWorthThree()
    {
        var settings = new RunSettings { IncludePostGameKingdoms = true };
        var rematch = Assert.Single(
            _repository.GetTalkatooCandidates("Mushroom", settings),
            moon => moon.Id == 33);
        var state = new GameState();
        state.Settings.IncludePostGameKingdoms = true;
        state.SetKingdom("Mushroom");
        Assert.True(state.TryAddPending(rematch));

        var outcome = state.MarkCollected(rematch);

        Assert.True(rematch.IsStory);
        Assert.True(rematch.IsMulti);
        Assert.Equal(3, rematch.MoonCountValue);
        Assert.Equal(CollectionOutcome.Counted, outcome);
        Assert.Equal(3, state.CountedMoonCount);
        Assert.Empty(state.Pending);
    }

    [Fact]
    public void CloudAndRuinedAreOnlyAvailableInPostgameModeInRouteOrder()
    {
        var normalKingdoms = _repository.GetKingdoms(new RunSettings
        {
            IncludePostGameKingdoms = false
        });
        var postgameKingdoms = _repository.GetKingdoms(new RunSettings
        {
            IncludePostGameKingdoms = true
        });

        Assert.DoesNotContain("Cloud", normalKingdoms);
        Assert.DoesNotContain("Ruined", normalKingdoms);
        Assert.True(postgameKingdoms.IndexOf("Cloud") < postgameKingdoms.IndexOf("Lost"));
        Assert.True(postgameKingdoms.IndexOf("Luncheon") < postgameKingdoms.IndexOf("Ruined"));
        Assert.True(postgameKingdoms.IndexOf("Ruined") < postgameKingdoms.IndexOf("Bowsers"));
    }
}
