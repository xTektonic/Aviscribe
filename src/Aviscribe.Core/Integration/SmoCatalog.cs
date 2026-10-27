using SMOVision.Core;

namespace Aviscribe.Core.Integration;

/// <summary>Preserves Aviscribe's persisted names and wire identities at the library boundary.</summary>
public static class SmoCatalog
{
    public static string KingdomName(KingdomId id) => id.Value switch
    {
        "bowsers" => "Bowsers", "dark-side" => "Dark", "darker-side" => "Darker",
        _ => char.ToUpperInvariant(id.Value[0]) + id.Value[1..]
    };

    public static KingdomId Kingdom(string name) => name == "Bowser" ? KingdomId.Bowsers : KingdomId.Parse(name);

    public static MoonId MoonId(Moon moon) => new(Kingdom(moon.Kingdom), moon.Id);

    public static string Language(GameLanguage language) => language switch
    {
        GameLanguage.English => "en", GameLanguage.ChineseTraditional => "zh-Hant",
        GameLanguage.ChineseSimplified => "zh-Hans", GameLanguage.Japanese => "ja",
        GameLanguage.Korean => "ko", GameLanguage.Dutch => "nl",
        GameLanguage.FrenchCanada => "fr-CA", GameLanguage.FrenchFrance => "fr-FR",
        GameLanguage.German => "de", GameLanguage.Italian => "it",
        GameLanguage.SpanishSpain => "es-ES", GameLanguage.SpanishLatinAmerica => "es-419",
        GameLanguage.Russian => "ru", _ => throw new ArgumentOutOfRangeException(nameof(language))
    };

    public static Moon Convert(SMOVision.Core.Moon moon) => new()
    {
        Id = moon.Id.LocalId, Kingdom = KingdomName(moon.Id.Kingdom),
        CollectionKingdom = moon.CollectionKingdom == moon.Id.Kingdom ? null : KingdomName(moon.CollectionKingdom),
        IsStory = moon.IsStory, IsMulti = moon.IsMulti,
        English = moon.GetName(Language(GameLanguage.English)).Text,
        ChineseTraditional = moon.GetName(Language(GameLanguage.ChineseTraditional)).Text,
        ChineseSimplified = moon.GetName(Language(GameLanguage.ChineseSimplified)).Text,
        Japanese = moon.GetName(Language(GameLanguage.Japanese)).Text,
        Korean = moon.GetName(Language(GameLanguage.Korean)).Text,
        Dutch = moon.GetName(Language(GameLanguage.Dutch)).Text,
        FrenchCanada = moon.GetName(Language(GameLanguage.FrenchCanada)).Text,
        FrenchFrance = moon.GetName(Language(GameLanguage.FrenchFrance)).Text,
        German = moon.GetName(Language(GameLanguage.German)).Text,
        Italian = moon.GetName(Language(GameLanguage.Italian)).Text,
        SpanishSpain = moon.GetName(Language(GameLanguage.SpanishSpain)).Text,
        SpanishLatinAmerica = moon.GetName(Language(GameLanguage.SpanishLatinAmerica)).Text,
        Russian = moon.GetName(Language(GameLanguage.Russian)).Text
    };
}
