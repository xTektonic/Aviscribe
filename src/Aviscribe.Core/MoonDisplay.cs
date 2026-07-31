namespace Aviscribe.Core
{
    public static class MoonDisplay
    {
        public static string Format(Moon moon, GameLanguage language = GameLanguage.English)
        {
            var name = moon.GetName(language);
            if (string.IsNullOrWhiteSpace(name))
                name = moon.English;

            var value = moon.MoonCountValue == 1 ? string.Empty : $" ({moon.MoonCountValue})";
            return $"{name}{value}";
        }
    }
}
