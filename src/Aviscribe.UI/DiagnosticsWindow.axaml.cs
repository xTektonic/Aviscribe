using Avalonia.Controls;
using Avalonia.Threading;
using Aviscribe.Core.Diagnostics;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;

namespace Aviscribe.UI;

public sealed record DiagnosticsSnapshot(
    string CaptureDevice,
    string CaptureStateAndFormat,
    string SourceAndCrop,
    string RequestedOcrMode,
    string ActiveOcrProvider);

public partial class DiagnosticsWindow : Window
{
    private readonly IAppDiagnostics _diagnostics;
    private readonly Func<DiagnosticsSnapshot> _snapshotProvider;
    private readonly DispatcherTimer _refreshTimer;

    public DiagnosticsWindow()
        : this(
            NullAppDiagnostics.Instance,
            () => new DiagnosticsSnapshot(
                "No capture device selected",
                "Stopped",
                "No source frame received",
                "CPU",
                "CPU"))
    {
    }

    public DiagnosticsWindow(
        IAppDiagnostics diagnostics,
        Func<DiagnosticsSnapshot> snapshotProvider)
    {
        _diagnostics = diagnostics;
        _snapshotProvider = snapshotProvider;
        _refreshTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _refreshTimer.Tick += (_, _) => RefreshDiagnostics();
        InitializeComponent();

        this.GetControl<Button>("btnRefreshDiagnostics").Click +=
            (_, _) => RefreshDiagnostics();
        this.GetControl<Button>("btnOpenLogFolder").Click +=
            (_, _) => OpenLogFolder();
        this.GetControl<Button>("btnCloseDiagnostics").Click +=
            (_, _) => Close();
        Opened += (_, _) =>
        {
            RefreshDiagnostics();
            _refreshTimer.Start();
        };
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private void RefreshDiagnostics()
    {
        var snapshot = _snapshotProvider();
        var entryAssembly = Assembly.GetEntryAssembly();
        var version = entryAssembly?
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ??
            entryAssembly?.GetName().Version?.ToString() ??
            "unknown";

        this.GetControl<TextBlock>("txtVersion").Text = version;
        this.GetControl<TextBlock>("txtApplication").Text =
            entryAssembly?.GetName().Name ?? "Aviscribe";
        this.GetControl<TextBlock>("txtRuntime").Text =
            $"{RuntimeInformation.FrameworkDescription}; " +
            $"{RuntimeInformation.OSDescription}; " +
            $"{RuntimeInformation.ProcessArchitecture}";
        this.GetControl<TextBlock>("txtCaptureDevice").Text =
            snapshot.CaptureDevice;
        this.GetControl<TextBlock>("txtCaptureState").Text =
            snapshot.CaptureStateAndFormat;
        this.GetControl<TextBlock>("txtSourceCrop").Text =
            snapshot.SourceAndCrop;
        this.GetControl<TextBlock>("txtRequestedOcrMode").Text = snapshot.RequestedOcrMode;
        this.GetControl<TextBlock>("txtActiveOcrProvider").Text = snapshot.ActiveOcrProvider;
        this.GetControl<TextBlock>("txtLogDirectory").Text =
            string.IsNullOrWhiteSpace(_diagnostics.LogDirectory)
                ? "Logging is unavailable in the designer."
                : _diagnostics.LogDirectory;
        this.GetControl<TextBox>("txtRecentLogs").Text = string.Join(
            Environment.NewLine,
            _diagnostics.RecentEntries
                .TakeLast(200)
                .Select(entry =>
                    $"{entry.Timestamp:HH:mm:ss} [{entry.Level}] {entry.Message}"));
    }

    private void OpenLogFolder()
    {
        if (string.IsNullOrWhiteSpace(_diagnostics.LogDirectory))
            return;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                UseShellExecute = false
            };
            if (OperatingSystem.IsWindows())
            {
                startInfo.FileName = "explorer.exe";
                startInfo.ArgumentList.Add(_diagnostics.LogDirectory);
            }
            else if (OperatingSystem.IsMacOS())
            {
                startInfo.FileName = "open";
                startInfo.ArgumentList.Add(_diagnostics.LogDirectory);
            }
            else
            {
                startInfo.FileName = "xdg-open";
                startInfo.ArgumentList.Add(_diagnostics.LogDirectory);
            }

            Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            _diagnostics.Error("Could not open the log directory.", ex);
            this.GetControl<TextBlock>("txtLogDirectory").Text =
                $"{_diagnostics.LogDirectory} (could not open: {ex.Message})";
        }
    }
}
