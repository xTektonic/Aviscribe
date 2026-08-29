namespace Aviscribe.Core;

public enum AppPlatform
{
    Windows,
    MacOS,
    Linux
}

public static class AppPaths
{
    public static string DataFolder =>
        Path.Combine(AppContext.BaseDirectory, "Data");

    public static string UserDataFolder => ResolveUserDataFolder(
        CurrentPlatform(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME"));

    public static string LogFolder => ResolveLogFolder(
        CurrentPlatform(),
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        Environment.GetEnvironmentVariable("XDG_STATE_HOME"));

    public static string TessData =>
        Path.Combine(DataFolder, "tessdata");

    public static string MoonList =>
        Path.Combine(DataFolder, "moon-list.json");

    public static string OcrModelPath =>
        Path.Combine(DataFolder, "rec.onnx");

    public static string CharsetPath =>
        Path.Combine(DataFolder, "dict.txt");

    public static string DetectorRulesPath =>
        Path.Combine(DataFolder, "detector-rules.json");

    public static string LinearDetectorModelPath =>
        Path.Combine(DataFolder, "linear-detector.json");

    public static string KingdomIconTemplateFolder =>
        Path.Combine(DataFolder, "KingdomIcons");

    public static string PendingOutputPath =>
        Path.Combine(UserDataFolder, "pending-moons.txt");

    public static string RunStatePath =>
        Path.Combine(UserDataFolder, "run-state.json");

    public static string AppPreferencesPath =>
        Path.Combine(UserDataFolder, "preferences.json");

    public static string OnlineResumePath =>
        Path.Combine(UserDataFolder, "online-resume.json");

    public static AppPlatform CurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
            return AppPlatform.Windows;
        if (OperatingSystem.IsMacOS())
            return AppPlatform.MacOS;
        if (OperatingSystem.IsLinux())
            return AppPlatform.Linux;

        throw new PlatformNotSupportedException(
            "Aviscribe supports Windows, macOS, and Linux.");
    }

    public static string ResolveUserDataFolder(
        AppPlatform platform,
        string homeFolder,
        string localAppData,
        string? xdgConfigHome = null)
    {
        return platform switch
        {
            AppPlatform.Windows => CombineWindows(
                string.IsNullOrWhiteSpace(localAppData)
                    ? CombineWindows(homeFolder, "AppData", "Local")
                    : localAppData,
                "Aviscribe"),
            AppPlatform.MacOS => CombineUnix(
                homeFolder,
                "Library",
                "Application Support",
                "Aviscribe"),
            AppPlatform.Linux => CombineUnix(
                string.IsNullOrWhiteSpace(xdgConfigHome)
                    ? CombineUnix(homeFolder, ".config")
                    : xdgConfigHome,
                "aviscribe"),
            _ => throw new ArgumentOutOfRangeException(nameof(platform))
        };
    }

    public static string ResolveLogFolder(
        AppPlatform platform,
        string homeFolder,
        string localAppData,
        string? xdgStateHome = null)
    {
        return platform switch
        {
            AppPlatform.Windows => CombineWindows(
                string.IsNullOrWhiteSpace(localAppData)
                    ? CombineWindows(homeFolder, "AppData", "Local")
                    : localAppData,
                "Aviscribe",
                "logs"),
            AppPlatform.MacOS => CombineUnix(
                homeFolder,
                "Library",
                "Logs",
                "Aviscribe"),
            AppPlatform.Linux => CombineUnix(
                string.IsNullOrWhiteSpace(xdgStateHome)
                    ? CombineUnix(homeFolder, ".local", "state")
                    : xdgStateHome,
                "aviscribe",
                "logs"),
            _ => throw new ArgumentOutOfRangeException(nameof(platform))
        };
    }

    private static string CombineWindows(params string[] parts)
    {
        var result = parts[0]
            .Replace('/', '\\')
            .TrimEnd('\\');
        foreach (var part in parts.Skip(1))
        {
            var segment = part.Trim('/', '\\');
            if (segment.Length > 0)
                result = $"{result}\\{segment}";
        }

        return result;
    }

    private static string CombineUnix(params string[] parts)
    {
        var result = parts[0].TrimEnd('/');
        foreach (var part in parts.Skip(1))
            result = $"{result}/{part.Trim('/', '\\')}";
        return result;
    }
}
