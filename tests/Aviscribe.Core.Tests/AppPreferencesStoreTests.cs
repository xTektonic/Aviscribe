namespace Aviscribe.Core.Tests;

public sealed class AppPreferencesStoreTests
{
    [Fact]
    public void MissingFileUsesFirstRunDefaults()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "AviscribeTests",
            Guid.NewGuid().ToString("N"),
            "preferences.json");

        var preferences = new AppPreferencesStore().Load(path);

        Assert.Equal(0, preferences.QuickStartVersionSeen);
    }

    [Fact]
    public void QuickStartVersionRoundTrips()
    {
        WithTemporaryPreferencesFile((path, store) =>
        {
            store.Save(path, new AppPreferences
            {
                QuickStartVersionSeen = AppPreferences.CurrentQuickStartVersion,
                Theme = AppThemePreference.Dark,
                AccentColor = AccentColorPreference.Teal
            });

            var restored = store.Load(path);

            Assert.Equal(
                AppPreferences.CurrentQuickStartVersion,
                restored.QuickStartVersionSeen);
            Assert.Equal(AppThemePreference.Dark, restored.Theme);
            Assert.Equal(AccentColorPreference.Teal, restored.AccentColor);
        });
    }

    [Fact]
    public void LegacyPreferencesUseDefaultQuickStartVersion()
    {
        WithTemporaryPreferencesFile((path, store) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "{}");

            var restored = store.Load(path);

            Assert.Equal(0, restored.QuickStartVersionSeen);
            Assert.Equal(AppThemePreference.System, restored.Theme);
            Assert.Equal(AccentColorPreference.System, restored.AccentColor);
        });
    }

    private static void WithTemporaryPreferencesFile(
        Action<string, AppPreferencesStore> action)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AviscribeTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "preferences.json");
        try
        {
            action(path, new AppPreferencesStore());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
