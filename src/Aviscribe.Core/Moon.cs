using System.Text.Json.Serialization;

namespace Aviscribe.Core
{
    public class Moon
    {
        public int Id { get; set; }
        public string Kingdom { get; set; } = string.Empty;

        [JsonPropertyName("collection_kingdom")]
        public string? CollectionKingdom { get; set; }

        [JsonPropertyName("is_story")]
        public bool IsStory { get; set; }

        [JsonPropertyName("is_multi")]
        public bool IsMulti { get; set; }

        [JsonIgnore]
        public bool IsHintArt => !string.IsNullOrWhiteSpace(CollectionKingdom);

        [JsonIgnore]
        public int MoonCountValue => IsMulti ? 3 : 1;

        public string English { get; set; } = string.Empty;

        [JsonPropertyName("chinese_traditional")]
        public string ChineseTraditional { get; set; } = string.Empty;

        [JsonPropertyName("chinese_simplified")]
        public string ChineseSimplified { get; set; } = string.Empty;

        public string Japanese { get; set; } = string.Empty;
        public string Korean { get; set; } = string.Empty;
        public string Dutch { get; set; } = string.Empty;

        [JsonPropertyName("french_canada")]
        public string FrenchCanada { get; set; } = string.Empty;

        [JsonPropertyName("french_france")]
        public string FrenchFrance { get; set; } = string.Empty;

        public string German { get; set; } = string.Empty;
        public string Italian { get; set; } = string.Empty;

        [JsonPropertyName("spanish_spain")]
        public string SpanishSpain { get; set; } = string.Empty;

        [JsonPropertyName("spanish_latin_america")]
        public string SpanishLatinAmerica { get; set; } = string.Empty;

        public string Russian { get; set; } = string.Empty;

        public bool IsCollectedInKingdom(string kingdom)
        {
            return Kingdom.Equals(kingdom, StringComparison.OrdinalIgnoreCase) ||
                   (CollectionKingdom?.Equals(kingdom, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        public string GetName(GameLanguage lang)
        {
            return lang switch
            {
                GameLanguage.English => English,
                GameLanguage.ChineseTraditional => ChineseTraditional,
                GameLanguage.ChineseSimplified => ChineseSimplified,
                GameLanguage.Japanese => Japanese,
                GameLanguage.Korean => Korean,
                GameLanguage.Dutch => Dutch,
                GameLanguage.FrenchCanada => FrenchCanada,
                GameLanguage.FrenchFrance => FrenchFrance,
                GameLanguage.German => German,
                GameLanguage.Italian => Italian,
                GameLanguage.SpanishSpain => SpanishSpain,
                GameLanguage.SpanishLatinAmerica => SpanishLatinAmerica,
                GameLanguage.Russian => Russian,
                _ => English
            };
        }
    }
}
