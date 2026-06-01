using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aviscribe.Core.Capture;

namespace Aviscribe.UI
{
    public partial class AviscribeApp : Application
    {
        private readonly IVideoProvider _captureProvider;

        public AviscribeApp()
            : this(new DesignVideoProvider())
        {
        }

        public AviscribeApp(IVideoProvider captureProvider)
        {
            _captureProvider = captureProvider;
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow(_captureProvider);
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
