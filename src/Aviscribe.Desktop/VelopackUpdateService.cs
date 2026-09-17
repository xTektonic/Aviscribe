using Aviscribe.Core;
using Velopack;
using Velopack.Sources;

namespace Aviscribe.Desktop;

internal sealed class VelopackUpdateService : IAppUpdateService
{
    private const string RepositoryUrl = "https://github.com/xTektonic/Aviscribe";
    private const string UpdateSourceOverrideVariable = "AVISCRIBE_UPDATE_SOURCE";
    private readonly UpdateManager _manager = CreateManager();
    private UpdateInfo? _pendingUpdate;

    public bool IsInstalled => _manager.IsInstalled;

    public async Task<AppUpdateInfo?> CheckForUpdatesAsync(
        CancellationToken cancellationToken)
    {
        _pendingUpdate = await _manager.CheckForUpdatesAsync().WaitAsync(cancellationToken);
        if (_pendingUpdate == null)
            return null;

        return new AppUpdateInfo(
            _manager.CurrentVersion?.ToString() ?? "unknown",
            _pendingUpdate.TargetFullRelease.Version.ToString());
    }

    public Task DownloadUpdateAsync(
        IProgress<int> progress,
        CancellationToken cancellationToken)
    {
        var update = _pendingUpdate ??
            throw new InvalidOperationException("No Aviscribe update has been selected.");
        return _manager.DownloadUpdatesAsync(
            update,
            progress.Report,
            cancellationToken);
    }

    public void ApplyUpdateAndRestart()
    {
        var update = _pendingUpdate ??
            throw new InvalidOperationException("No Aviscribe update has been downloaded.");
        _manager.ApplyUpdatesAndRestart(update.TargetFullRelease);
    }

    private static UpdateManager CreateManager()
    {
        var updateSourceOverride =
            Environment.GetEnvironmentVariable(UpdateSourceOverrideVariable);
        return string.IsNullOrWhiteSpace(updateSourceOverride)
            ? new UpdateManager(
                new GithubSource(
                    RepositoryUrl,
                    accessToken: null,
                    prerelease: false))
            : new UpdateManager(updateSourceOverride);
    }
}
