using Avalonia;
using Aviscribe.Core.Capture;
using Aviscribe.UI;
using Aviscribe.Windows.Capture;

namespace Aviscribe.Windows.App
{
    internal class Program
    {
        [STAThread]
        public static void Main(string[] args)
        {
            IVideoProvider provider = new AccordVideoProvider();

            BuildAvaloniaApp(provider)
                .StartWithClassicDesktopLifetime(args);
        }

        private static AppBuilder BuildAvaloniaApp(IVideoProvider provider)
        {
            return AppBuilder
                .Configure(() => new AviscribeApp(provider))
                .UsePlatformDetect()
                .WithInterFont()
                .LogToTrace();
        }
    }
}