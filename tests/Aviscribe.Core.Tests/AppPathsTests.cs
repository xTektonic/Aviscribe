using Aviscribe.Core;

namespace Aviscribe.Core.Tests;

public sealed class AppPathsTests
{
    [Fact]
    public void ResolvesWindowsFolders()
    {
        Assert.Equal(
            @"C:\Users\runner\AppData\Local\Aviscribe",
            AppPaths.ResolveUserDataFolder(
                AppPlatform.Windows,
                @"C:\Users\runner",
                @"C:\Users\runner\AppData\Local"));
        Assert.Equal(
            @"C:\Users\runner\AppData\Local\Aviscribe\logs",
            AppPaths.ResolveLogFolder(
                AppPlatform.Windows,
                @"C:\Users\runner",
                @"C:\Users\runner\AppData\Local"));
        Assert.Equal(
            @"C:\Users\runner\AppData\Local\Aviscribe",
            AppPaths.ResolveUserDataFolder(
                AppPlatform.Windows,
                @"C:\Users\runner",
                ""));
    }

    [Fact]
    public void ResolvesMacFolders()
    {
        Assert.Equal(
            "/Users/runner/Library/Application Support/Aviscribe",
            AppPaths.ResolveUserDataFolder(
                AppPlatform.MacOS,
                "/Users/runner",
                ""));
        Assert.Equal(
            "/Users/runner/Library/Logs/Aviscribe",
            AppPaths.ResolveLogFolder(
                AppPlatform.MacOS,
                "/Users/runner",
                ""));
    }

    [Fact]
    public void ResolvesLinuxXdgFoldersAndFallbacks()
    {
        Assert.Equal(
            "/var/config/aviscribe",
            AppPaths.ResolveUserDataFolder(
                AppPlatform.Linux,
                "/home/runner",
                "",
                "/var/config"));
        Assert.Equal(
            "/home/runner/.config/aviscribe",
            AppPaths.ResolveUserDataFolder(
                AppPlatform.Linux,
                "/home/runner",
                ""));
        Assert.Equal(
            "/var/state/aviscribe/logs",
            AppPaths.ResolveLogFolder(
                AppPlatform.Linux,
                "/home/runner",
                "",
                "/var/state"));
        Assert.Equal(
            "/home/Zoë/.config/aviscribe",
            AppPaths.ResolveUserDataFolder(
                AppPlatform.Linux,
                "/home/Zoë",
                ""));
    }
}
