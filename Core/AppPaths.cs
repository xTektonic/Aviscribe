public static class AppPaths
{
    public static string DataFolder =>
        Path.Combine(AppContext.BaseDirectory, "Data");

    public static string TessData =>
        Path.Combine(DataFolder, "tessdata");

    public static string MoonList =>
        Path.Combine(DataFolder, "moon-list.json");
}