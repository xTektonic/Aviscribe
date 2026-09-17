using Aviscribe.Core.Diagnostics;

namespace Aviscribe.Core;

public sealed record AppUpdateInfo(string CurrentVersion, string AvailableVersion);

public interface IAppUpdateService
{
    bool IsInstalled { get; }

    Task<AppUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken);

    Task DownloadUpdateAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken);

    void ApplyUpdateAndRestart();
}

public interface IUpdateInteraction
{
    Task<bool> ConfirmUpdateAsync(
        AppUpdateInfo update,
        CancellationToken cancellationToken);

    Task<bool> DownloadUpdateAsync(
        Func<IProgress<int>, CancellationToken, Task> download,
        CancellationToken cancellationToken);

    Task ShowDownloadErrorAsync(
        string message,
        CancellationToken cancellationToken);
}

public interface IStartupMaintenanceService
{
    Task RunAsync(CancellationToken cancellationToken);
}

public sealed class StartupUpdateCoordinator
{
    private readonly IAppUpdateService _updates;
    private readonly IAppDiagnostics _diagnostics;

    public StartupUpdateCoordinator(
        IAppUpdateService updates,
        IAppDiagnostics diagnostics)
    {
        _updates = updates;
        _diagnostics = diagnostics;
    }

    public async Task RunAsync(
        IUpdateInteraction interaction,
        CancellationToken cancellationToken)
    {
        if (!_updates.IsInstalled)
        {
            _diagnostics.Debug("Skipping update check because Aviscribe is not running from a Velopack installation.");
            return;
        }

        AppUpdateInfo? update;
        try
        {
            _diagnostics.Information("Checking for Aviscribe updates.");
            update = await _updates.CheckForUpdatesAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            _diagnostics.Error("Could not check for Aviscribe updates.", ex);
            return;
        }

        if (update == null)
        {
            _diagnostics.Information("Aviscribe is up to date.");
            return;
        }

        _diagnostics.Information(
            $"Aviscribe {update.AvailableVersion} is available; current version is {update.CurrentVersion}.");
        if (!await interaction.ConfirmUpdateAsync(update, cancellationToken))
        {
            _diagnostics.Information("The available update was deferred until the next startup.");
            return;
        }

        try
        {
            var downloaded = await interaction.DownloadUpdateAsync(
                _updates.DownloadUpdateAsync,
                cancellationToken);
            if (!downloaded)
            {
                _diagnostics.Information("The update download was cancelled.");
                return;
            }

            _diagnostics.Information("The update was downloaded; applying it and restarting Aviscribe.");
            _updates.ApplyUpdateAndRestart();
        }
        catch (OperationCanceledException)
        {
            _diagnostics.Information("The update download was cancelled.");
        }
        catch (Exception ex)
        {
            _diagnostics.Error("Could not download or apply the Aviscribe update.", ex);
            await interaction.ShowDownloadErrorAsync(ex.Message, cancellationToken);
        }
    }
}

public sealed class DisabledAppUpdateService : IAppUpdateService
{
    public static DisabledAppUpdateService Instance { get; } = new();

    private DisabledAppUpdateService()
    {
    }

    public bool IsInstalled => false;

    public Task<AppUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken) =>
        Task.FromResult<AppUpdateInfo?>(null);

    public Task DownloadUpdateAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken) => Task.CompletedTask;

    public void ApplyUpdateAndRestart()
    {
    }
}

public sealed class NoOpStartupMaintenanceService : IStartupMaintenanceService
{
    public static NoOpStartupMaintenanceService Instance { get; } = new();

    private NoOpStartupMaintenanceService()
    {
    }

    public Task RunAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
