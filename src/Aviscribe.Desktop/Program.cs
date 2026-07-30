using Avalonia;
using Aviscribe.Capture;
using Aviscribe.UI;

namespace Aviscribe.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    private static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder
            .Configure(() => new AviscribeApp(new FlashCapVideoProvider()))
            .UsePlatformDetect()
            .WithInterFont();
    }
}
