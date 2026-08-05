namespace Aviscribe.Core;

public sealed class AppPreferences
{
    public const int CurrentQuickStartVersion = 1;

    public int QuickStartVersionSeen { get; set; }
    public AppThemePreference Theme { get; set; } = AppThemePreference.System;
    public AccentColorPreference AccentColor { get; set; } = AccentColorPreference.System;
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
