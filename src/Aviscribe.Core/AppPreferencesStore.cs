using System.Text.Json;

namespace Aviscribe.Core;

public sealed class AppPreferencesStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true
    };

    public AppPreferences Load(string path)
    {
        if (!File.Exists(path))
            return new AppPreferences();

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<AppPreferences>(json, JsonOptions) ??
            new AppPreferences();
    }

    public void Save(string path, AppPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(preferences, JsonOptions));
        File.Move(tempPath, path, overwrite: true);
    }
}
