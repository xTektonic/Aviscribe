using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;

namespace Aviscribe.UI
{
    public partial class AviscribeApp : Application
    {
        private readonly IVideoProvider _captureProvider;
        private readonly IAppDiagnostics _diagnostics;

        public AviscribeApp()
            : this(new DesignVideoProvider(), NullAppDiagnostics.Instance)
        {
        }

        public AviscribeApp(
            IVideoProvider captureProvider,
            IAppDiagnostics? diagnostics = null)
        {
            _captureProvider = captureProvider;
            _diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
        }

        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow(
                    _captureProvider,
                    _diagnostics);
                desktop.Exit += (_, _) => _diagnostics.Dispose();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
