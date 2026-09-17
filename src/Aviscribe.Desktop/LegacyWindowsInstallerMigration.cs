using Aviscribe.Core;
using Aviscribe.Core.Diagnostics;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Velopack.Windows;

namespace Aviscribe.Desktop;

internal sealed class LegacyWindowsInstallerMigration : IStartupMaintenanceService
{
    private const string LegacyUpgradeCode = "{BCBFA9D2-4673-4C63-A7BC-77A27BC49672}";
    private const uint ErrorSuccess = 0;
    private const uint ErrorNoMoreItems = 259;
    private const int ErrorCancelled = 1223;
    private const int ErrorSuccessRebootRequired = 3010;

    private readonly IAppUpdateService _updates;
    private readonly IAppDiagnostics _diagnostics;

    public LegacyWindowsInstallerMigration(
        IAppUpdateService updates,
        IAppDiagnostics diagnostics)
    {
        _updates = updates;
        _diagnostics = diagnostics;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        if (!OperatingSystem.IsWindows() || !_updates.IsInstalled)
            return;

        var products = EnumerateLegacyProducts();
        foreach (var productCode in products)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                _diagnostics.Information($"Removing legacy Aviscribe installation {productCode}.");
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "msiexec.exe",
                    Arguments = $"/x {productCode} /qn /norestart",
                    UseShellExecute = true,
                    Verb = "runas"
                });
                if (process == null)
                    throw new InvalidOperationException("Windows Installer did not start.");

                await process.WaitForExitAsync(cancellationToken);
                if (process.ExitCode != 0 &&
                    process.ExitCode != ErrorSuccessRebootRequired)
                    throw new InvalidOperationException(
                        $"Windows Installer exited with code {process.ExitCode}.");

#pragma warning disable CS0618
                new Shortcuts().CreateShortcutForThisExe(ShortcutLocation.StartMenuRoot);
#pragma warning restore CS0618
                _diagnostics.Information("The legacy Aviscribe installation was removed.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                _diagnostics.Information("Legacy Aviscribe removal was declined; it will be offered again later.");
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _diagnostics.Error("Could not remove the legacy Aviscribe installation; it will be retried later.", ex);
                return;
            }
        }
    }

    private static IReadOnlyList<string> EnumerateLegacyProducts()
    {
        var products = new List<string>();
        for (uint index = 0; ; index++)
        {
            var productCode = new StringBuilder(39);
            var result = MsiEnumRelatedProducts(
                LegacyUpgradeCode,
                0,
                index,
                productCode);
            if (result == ErrorNoMoreItems)
                break;
            if (result != ErrorSuccess)
                throw new Win32Exception((int)result);
            products.Add(productCode.ToString());
        }

        return products;
    }

    [DllImport("msi.dll", CharSet = CharSet.Unicode)]
    private static extern uint MsiEnumRelatedProducts(
        string upgradeCode,
        uint reserved,
        uint productIndex,
        StringBuilder productCode);
}
