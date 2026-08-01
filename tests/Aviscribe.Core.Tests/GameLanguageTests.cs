using System.Text;

namespace Aviscribe.Core.Tests;

public class GameLanguageTests
{
    [Fact]
    public void InputLanguages_OnlySupportChineseVariants()
    {
        var supportedLanguages = Enum.GetValues<GameLanguage>()
            .Where(GameLanguageCatalog.IsSupportedInputLanguage);

        Assert.Equal(
            [GameLanguage.ChineseTraditional, GameLanguage.ChineseSimplified],
            supportedLanguages);
    }

    [Fact]
    public void DefaultMoonList_OffersEveryPopulatedLanguage()
    {
        var repository = MoonRepository.LoadDefault();

        Assert.Equal(Enum.GetValues<GameLanguage>(), repository.GetAvailableLanguages());
    }

    [Fact]
    public void AvailableLanguages_ExcludesLanguagesWithNoNames()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(
                path,
                """[{"id":1,"kingdom":"Cap","english":"A Moon","japanese":""}]""",
                Encoding.UTF8);

            var repository = MoonRepository.Load(path);

            Assert.Equal([GameLanguage.English], repository.GetAvailableLanguages());
        }
        finally
        {
            File.Delete(path);
        }
    }
}
