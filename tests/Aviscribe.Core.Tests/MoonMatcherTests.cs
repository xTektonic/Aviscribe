namespace Aviscribe.Core.Tests;

public class MoonMatcherTests
{
    [Theory]
    [InlineData(GameLanguage.ChineseSimplified, "沙之国的嘀嗒·运动1", "Sand Kingdom Timer Challenge 1")]
    [InlineData(GameLanguage.ChineseTraditional, "沙之國的滴答‧運動3", "Sand Kingdom Timer Challenge 3")]
    public void Match_UsesTrailingAsciiNumeralForChineseNumberedVariants(
        GameLanguage inputLanguage,
        string input,
        string expectedEnglishName)
    {
        var repository = MoonRepository.LoadDefault();
        var matcher = new MoonMatcher(repository, inputLanguage, GameLanguage.English);

        var result = matcher.Match(input, "Sand");

        Assert.False(result.IsAmbiguous);
        Assert.Equal(expectedEnglishName, result.BestMatch?.English);
    }

    [Theory]
    [InlineData("沙之国的嘀嗒·运动")]
    [InlineData("沙之国的嘀嗒·运动III")]
    public void Match_DoesNotResolveNumberedVariantsWithoutTrailingArabicNumeral(string input)
    {
        var repository = MoonRepository.LoadDefault();
        var matcher = new MoonMatcher(
            repository,
            GameLanguage.ChineseSimplified,
            GameLanguage.English);

        var result = matcher.Match(input, "Sand");

        Assert.True(result.IsAmbiguous);
        Assert.Null(result.BestMatch);
    }

    [Fact]
    public void Match_TrailingNumeralResolvesVariantsEvenWhenScoresAreVeryClose()
    {
        var repository = MoonRepository.LoadDefault();
        var matcher = new MoonMatcher(
            repository,
            GameLanguage.ChineseSimplified,
            GameLanguage.English);
        var variants = new[]
        {
            new Moon
            {
                Id = 1,
                Kingdom = "Test",
                English = "Long Challenge 1",
                ChineseSimplified = "这是一个非常长且完全相同的月亮挑战名称１"
            },
            new Moon
            {
                Id = 2,
                Kingdom = "Test",
                English = "Long Challenge 2",
                ChineseSimplified = "这是一个非常长且完全相同的月亮挑战名称２"
            }
        };

        var result = matcher.Match("这是一个非常长且完全相同的月亮挑战名称2", variants);

        Assert.False(result.IsAmbiguous);
        Assert.Equal(2, result.BestMatch?.Id);
    }
}
