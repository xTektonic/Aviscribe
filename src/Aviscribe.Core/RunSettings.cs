namespace Aviscribe.Core
{
    public class RunSettings
    {
        public RunCategory Category { get; set; } = RunCategory.Standard;
        public bool IncludePostGameKingdoms { get; set; }
        public GameLanguage InputLanguage { get; set; } = GameLanguage.ChineseTraditional;
        public GameLanguage OutputLanguage { get; set; } = GameLanguage.English;
        public bool WoodedBeforeLake { get; set; } = true;
        public bool SeasideBeforeSnow { get; set; } = true;
        public bool ShowPendingMoonImages { get; set; }
        public bool DebugLogging { get; set; }
        public string FocusMoonNumberHotkey { get; set; } = "M";
        public string MoveToPendingHotkey { get; set; } = "P";
        public string MoveToCountedHotkey { get; set; } = "C";
        public string MoveToWrongHotkey { get; set; } = "W";
        public string RemoveMoonHotkey { get; set; } = "X";

        public bool AllowsStoryMoons => Category != RunCategory.Hardcore;

        public RunSettings Clone()
        {
            return new RunSettings
            {
                Category = Category,
                IncludePostGameKingdoms = IncludePostGameKingdoms,
                InputLanguage = InputLanguage,
                OutputLanguage = OutputLanguage,
                WoodedBeforeLake = WoodedBeforeLake,
                SeasideBeforeSnow = SeasideBeforeSnow,
                ShowPendingMoonImages = ShowPendingMoonImages,
                DebugLogging = DebugLogging,
                FocusMoonNumberHotkey = FocusMoonNumberHotkey,
                MoveToPendingHotkey = MoveToPendingHotkey,
                MoveToCountedHotkey = MoveToCountedHotkey,
                MoveToWrongHotkey = MoveToWrongHotkey,
                RemoveMoonHotkey = RemoveMoonHotkey
            };
        }

        public void CopyFrom(RunSettings settings)
        {
            Category = settings.Category;
            IncludePostGameKingdoms = settings.IncludePostGameKingdoms;
            InputLanguage = settings.InputLanguage;
            OutputLanguage = settings.OutputLanguage;
            WoodedBeforeLake = settings.WoodedBeforeLake;
            SeasideBeforeSnow = settings.SeasideBeforeSnow;
            ShowPendingMoonImages = settings.ShowPendingMoonImages;
            DebugLogging = settings.DebugLogging;
            FocusMoonNumberHotkey = settings.FocusMoonNumberHotkey;
            MoveToPendingHotkey = settings.MoveToPendingHotkey;
            MoveToCountedHotkey = settings.MoveToCountedHotkey;
            MoveToWrongHotkey = settings.MoveToWrongHotkey;
            RemoveMoonHotkey = settings.RemoveMoonHotkey;
        }
    }
}
