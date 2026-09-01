using Aviscribe.Core.Online;

namespace Aviscribe.Core.Tests;

public sealed class RunStateTextFormatterTests
{
    [Fact]
    public void PendingOutputCanBeFilteredToLocallyOwnedHints()
    {
        var repository = MoonRepository.LoadDefault();
        var state = new GameState();
        state.SetKingdom(GameState.InitialKingdom);
        var runs = new RunCoordinator(state, repository);
        var moons = repository.Moons
            .Where(moon => moon.Kingdom == state.CurrentKingdom)
            .Take(2)
            .ToArray();
        runs.SetPending(moons[0]);
        runs.SetPending(moons[1]);

        var text = RunStateTextFormatter.FormatPending(
            state.CreateSnapshot(),
            GameLanguage.English,
            moon => moon.Id == moons[1].Id);

        Assert.DoesNotContain(moons[0].English, text);
        Assert.Equal(MoonDisplay.Format(moons[1], GameLanguage.English), text);
    }
}
