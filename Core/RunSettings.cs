namespace Aviscribe.Core
{
    public class RunSettings
    {
        public RunCategory Category { get; set; } = RunCategory.Standard;
        public bool IncludePostGameKingdoms { get; set; }
        public GameLanguage InputLanguage { get; set; } = GameLanguage.ChineseTraditional;
        public GameLanguage OutputLanguage { get; set; } = GameLanguage.English;

        public bool AllowsStoryMoons => Category != RunCategory.Hardcore;

        public RunSettings Clone()
        {
            return new RunSettings
            {
                Category = Category,
                IncludePostGameKingdoms = IncludePostGameKingdoms,
                InputLanguage = InputLanguage,
                OutputLanguage = OutputLanguage
            };
        }

        public void CopyFrom(RunSettings settings)
        {
            Category = settings.Category;
            IncludePostGameKingdoms = settings.IncludePostGameKingdoms;
            InputLanguage = settings.InputLanguage;
            OutputLanguage = settings.OutputLanguage;
        }
    }
}
