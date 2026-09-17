using Avalonia;
using Aviscribe.Capture;
using Aviscribe.Core;
using Aviscribe.Core.Diagnostics;
using Aviscribe.Core.Capture;
using Aviscribe.UI;
using System.Runtime.InteropServices;
using Velopack;

namespace Aviscribe.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().Run();

        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        var diagnostics = CreateDiagnostics();
        diagnostics.Information(
            $"Starting Aviscribe on {RuntimeInformation.OSDescription} " +
            $"({RuntimeInformation.ProcessArchitecture}).");
        var updates = new VelopackUpdateService();

        return AppBuilder
            .Configure(() => new AviscribeApp(
                new CompositeVideoProvider(
                    new FlashCapVideoProvider(),
                    PlatformWindowCaptureProvider.Create(diagnostics)),
                diagnostics,
                updates,
                new LegacyWindowsInstallerMigration(updates, diagnostics)))
            .UsePlatformDetect()
            .WithInterFont();
    }

    private static IAppDiagnostics CreateDiagnostics()
    {
        try
        {
            return new FileAppDiagnostics(AppPaths.LogFolder);
        }
        catch
        {
            return NullAppDiagnostics.Instance;
        }
    }
}
