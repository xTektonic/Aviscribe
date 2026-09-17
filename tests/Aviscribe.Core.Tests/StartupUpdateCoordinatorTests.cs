using Aviscribe.Core.Diagnostics;

namespace Aviscribe.Core.Tests;

public sealed class StartupUpdateCoordinatorTests
{
    [Fact]
    public async Task UninstalledAppSkipsCheck()
    {
        var updates = new FakeUpdates { IsInstalled = false };
        var interaction = new FakeInteraction();

        await RunAsync(updates, interaction);

        Assert.Equal(0, updates.CheckCount);
        Assert.Equal(0, interaction.ConfirmCount);
    }

    [Fact]
    public async Task NoUpdateContinuesWithoutPrompt()
    {
        var updates = new FakeUpdates();
        var interaction = new FakeInteraction();

        await RunAsync(updates, interaction);

        Assert.Equal(1, updates.CheckCount);
        Assert.Equal(0, interaction.ConfirmCount);
    }

    [Fact]
    public async Task DeclinedUpdateIsCheckedAgainOnNextStartup()
    {
        var updates = AvailableUpdate();
        var interaction = new FakeInteraction { Confirm = false };
        var coordinator = new StartupUpdateCoordinator(
            updates,
            NullAppDiagnostics.Instance);

        await coordinator.RunAsync(interaction, CancellationToken.None);
        await coordinator.RunAsync(interaction, CancellationToken.None);

        Assert.Equal(2, updates.CheckCount);
        Assert.Equal(2, interaction.ConfirmCount);
        Assert.Equal(0, updates.DownloadCount);
    }

    [Fact]
    public async Task AcceptedUpdateDownloadsAppliesAndRestarts()
    {
        var updates = AvailableUpdate();
        var interaction = new FakeInteraction { Confirm = true };

        await RunAsync(updates, interaction);

        Assert.Equal(1, updates.DownloadCount);
        Assert.True(updates.Applied);
        Assert.Equal(0, interaction.ErrorCount);
    }

    [Fact]
    public async Task CancelledDownloadDoesNotApply()
    {
        var updates = AvailableUpdate();
        var interaction = new FakeInteraction
        {
            Confirm = true,
            CompleteDownload = false
        };

        await RunAsync(updates, interaction);

        Assert.Equal(0, updates.DownloadCount);
        Assert.False(updates.Applied);
    }

    [Fact]
    public async Task CheckFailureIsSilentToUser()
    {
        var updates = new FakeUpdates
        {
            CheckFailure = new HttpRequestException("offline")
        };
        var interaction = new FakeInteraction();

        await RunAsync(updates, interaction);

        Assert.Equal(0, interaction.ConfirmCount);
        Assert.Equal(0, interaction.ErrorCount);
    }

    [Fact]
    public async Task DownloadFailureShowsErrorAndDoesNotApply()
    {
        var updates = AvailableUpdate();
        updates.DownloadFailure = new IOException("download failed");
        var interaction = new FakeInteraction { Confirm = true };

        await RunAsync(updates, interaction);

        Assert.False(updates.Applied);
        Assert.Equal(1, interaction.ErrorCount);
        Assert.Contains("download failed", interaction.LastError);
    }

    private static FakeUpdates AvailableUpdate() => new()
    {
        Update = new AppUpdateInfo("1.0.6", "1.1.0")
    };

    private static Task RunAsync(
        FakeUpdates updates,
        FakeInteraction interaction) =>
        new StartupUpdateCoordinator(updates, NullAppDiagnostics.Instance)
            .RunAsync(interaction, CancellationToken.None);

    private sealed class FakeUpdates : IAppUpdateService
    {
        public bool IsInstalled { get; init; } = true;
        public AppUpdateInfo? Update { get; init; }
        public Exception? CheckFailure { get; init; }
        public Exception? DownloadFailure { get; set; }
        public int CheckCount { get; private set; }
        public int DownloadCount { get; private set; }
        public bool Applied { get; private set; }

        public Task<AppUpdateInfo?> CheckForUpdatesAsync(CancellationToken cancellationToken)
        {
            CheckCount++;
            return CheckFailure == null
                ? Task.FromResult(Update)
                : Task.FromException<AppUpdateInfo?>(CheckFailure);
        }

        public Task DownloadUpdateAsync(
            IProgress<int> progress,
            CancellationToken cancellationToken)
        {
            DownloadCount++;
            progress.Report(50);
            return DownloadFailure == null
                ? Task.CompletedTask
                : Task.FromException(DownloadFailure);
        }

        public void ApplyUpdateAndRestart()
        {
            Applied = true;
        }
    }

    private sealed class FakeInteraction : IUpdateInteraction
    {
        public bool Confirm { get; init; }
        public bool CompleteDownload { get; init; } = true;
        public int ConfirmCount { get; private set; }
        public int ErrorCount { get; private set; }
        public string LastError { get; private set; } = string.Empty;

        public Task<bool> ConfirmUpdateAsync(
            AppUpdateInfo update,
            CancellationToken cancellationToken)
        {
            ConfirmCount++;
            return Task.FromResult(Confirm);
        }

        public async Task<bool> DownloadUpdateAsync(
            Func<IProgress<int>, CancellationToken, Task> download,
            CancellationToken cancellationToken)
        {
            if (!CompleteDownload)
                return false;

            await download(new Progress<int>(), cancellationToken);
            return true;
        }

        public Task ShowDownloadErrorAsync(
            string message,
            CancellationToken cancellationToken)
        {
            ErrorCount++;
            LastError = message;
            return Task.CompletedTask;
        }
    }
}
