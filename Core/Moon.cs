using System.Text.Json.Serialization;

namespace Aviscribe.Core
{
    public class Moon
    {
        public int Id { get; set; }
        public string Kingdom { get; set; }

        public string English { get; set; }

        [JsonPropertyName("chinese_traditional")]
        public string ChineseTraditional { get; set; }

        [JsonPropertyName("chinese_simplified")]
        public string ChineseSimplified { get; set; }

        public string Japanese { get; set; }
        public string Korean { get; set; }
        public string Dutch { get; set; }

        [JsonPropertyName("french_canada")]
        public string FrenchCanada { get; set; }

        [JsonPropertyName("french_france")]
        public string FrenchFrance { get; set; }

        public string German { get; set; }
        public string Italian { get; set; }

        [JsonPropertyName("spanish_spain")]
        public string SpanishSpain { get; set; }

        [JsonPropertyName("spanish_latin_america")]
        public string SpanishLatinAmerica { get; set; }

        public string Russian { get; set; }

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
