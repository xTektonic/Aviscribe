namespace Aviscribe.Core
{
    public class MoonQueryOptions
    {
        public string? Kingdom { get; set; }
        public bool IncludeStory { get; set; } = true;
        public bool IncludeNonStory { get; set; } = true;
        public bool IncludeHintArt { get; set; } = true;
        public bool IncludePostGameKingdoms { get; set; }
        public bool MatchCollectionKingdom { get; set; }
    }
}
