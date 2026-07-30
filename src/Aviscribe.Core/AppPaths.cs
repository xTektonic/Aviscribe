public static class AppPaths
{
    public static string DataFolder =>
        Path.Combine(AppContext.BaseDirectory, "Data");

    public static string UserDataFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aviscribe");

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

    public static string PendingOutputPath =>
        Path.Combine(UserDataFolder, "pending-moons.txt");

    public static string RunStatePath =>
        Path.Combine(UserDataFolder, "run-state.json");
}
