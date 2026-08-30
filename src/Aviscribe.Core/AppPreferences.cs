namespace Aviscribe.Core;

public sealed class AppPreferences
{
    public const int CurrentQuickStartVersion = 2;

    public int QuickStartVersionSeen { get; set; }
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;
    public AccentColorPreference AccentColor { get; set; } = AccentColorPreference.System;
    public TextSizePreference TextSize { get; set; } =
        TextSizePreference.Default;
    public string OnlineServerAddress { get; set; } = string.Empty;
    public int OnlineServerPort { get; set; }
    public string OnlineDisplayName { get; set; } = string.Empty;
}

public enum AppThemePreference
{
    System,
    Light,
    Dark
}

public enum AccentColorPreference
{
    System,
    Blue,
    Teal,
    Green,
    Orange,
    Red,
    Purple
}

public enum TextSizePreference
{
    Default,
    Small,
    Large,
    ExtraLarge
}
