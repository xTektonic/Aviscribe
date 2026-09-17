using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;

namespace Aviscribe.UI
{
    public partial class AviscribeApp : Application
    {
        private readonly IVideoProvider _captureProvider;
        private readonly IAppDiagnostics _diagnostics;
        private readonly IAppUpdateService _updates;
        private readonly IStartupMaintenanceService _startupMaintenance;

        public AviscribeApp()
            : this(
                new DesignVideoProvider(),
                NullAppDiagnostics.Instance,
                DisabledAppUpdateService.Instance,
                NoOpStartupMaintenanceService.Instance)
        {
        }

        public AviscribeApp(
            IVideoProvider captureProvider,
            IAppDiagnostics? diagnostics = null,
            IAppUpdateService? updates = null,
            IStartupMaintenanceService? startupMaintenance = null)
        {
            _captureProvider = captureProvider;
            _diagnostics = diagnostics ?? NullAppDiagnostics.Instance;
            _updates = updates ?? DisabledAppUpdateService.Instance;
            _startupMaintenance = startupMaintenance ?? NoOpStartupMaintenanceService.Instance;
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
                    _diagnostics,
                    _updates,
                    _startupMaintenance);
                desktop.Exit += (_, _) => _diagnostics.Dispose();
            }

            base.OnFrameworkInitializationCompleted();
        }
    }
}
