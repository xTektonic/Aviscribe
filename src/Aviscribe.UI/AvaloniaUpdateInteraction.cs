using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using Aviscribe.Core;

namespace Aviscribe.UI;

internal sealed class AvaloniaUpdateInteraction : IUpdateInteraction
{
    private readonly Window _owner;

    public AvaloniaUpdateInteraction(Window owner)
    {
        _owner = owner;
    }

    public async Task<bool> ConfirmUpdateAsync(
        AppUpdateInfo update,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = CreateDialog("Aviscribe Update", 470, 230);
        var updateButton = new Button
        {
            Content = "Update and restart",
            IsDefault = true
        };
        var notNowButton = new Button
        {
            Content = "Not now",
            IsCancel = true
        };
        updateButton.Click += (_, _) => dialog.Close(true);
        notNowButton.Click += (_, _) => dialog.Close(false);
        dialog.Content = CreateDialogContent(
            "An update is available",
            $"Aviscribe {update.AvailableVersion} is available. You are currently using {update.CurrentVersion}.",
            notNowButton,
            updateButton);

        using var registration = cancellationToken.Register(
            () => Dispatcher.UIThread.Post(() => dialog.Close(false)));
        return await dialog.ShowDialog<bool>(_owner);
    }

    public async Task<bool> DownloadUpdateAsync(
        Func<IProgress<int>, CancellationToken, Task> download,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var downloadCancellation =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var dialog = CreateDialog("Downloading Aviscribe Update", 470, 210);
        var progressBar = new ProgressBar
        {
            Minimum = 0,
            Maximum = 100,
            Height = 12
        };
        var progressText = new TextBlock { Text = "Preparing download…" };
        var cancelButton = new Button
        {
            Content = "Cancel",
            IsCancel = true,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        cancelButton.Click += (_, _) =>
        {
            cancelButton.IsEnabled = false;
            progressText.Text = "Cancelling…";
            downloadCancellation.Cancel();
        };
        dialog.Closing += (_, _) => downloadCancellation.Cancel();
        dialog.Content = new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 14,
            Children =
            {
                new TextBlock
                {
                    Text = "Downloading update",
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold
                },
                progressText,
                progressBar,
                cancelButton
            }
        };

        Exception? failure = null;
        dialog.Opened += async (_, _) =>
        {
            try
            {
                var progress = new Progress<int>(value =>
                {
                    var bounded = Math.Clamp(value, 0, 100);
                    progressBar.Value = bounded;
                    progressText.Text = $"Downloaded {bounded}%";
                });
                await download(progress, downloadCancellation.Token);
                dialog.Close(true);
            }
            catch (OperationCanceledException)
            {
                dialog.Close(false);
            }
            catch (Exception ex)
            {
                failure = ex;
                dialog.Close(false);
            }
        };

        var completed = await dialog.ShowDialog<bool>(_owner);
        if (failure != null)
            throw failure;
        return completed;
    }

    public async Task ShowDownloadErrorAsync(
        string message,
        CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return;

        var dialog = CreateDialog("Update Failed", 470, 220);
        var closeButton = new Button
        {
            Content = "Close",
            IsDefault = true,
            IsCancel = true
        };
        closeButton.Click += (_, _) => dialog.Close();
        dialog.Content = CreateDialogContent(
            "Aviscribe could not be updated",
            $"The current version is still available. Aviscribe will try again next time it starts.\n\n{message}",
            closeButton);
        await dialog.ShowDialog(_owner);
    }

    private Window CreateDialog(string title, double width, double height) => new()
    {
        Title = title,
        Width = width,
        Height = height,
        CanResize = false,
        WindowStartupLocation = WindowStartupLocation.CenterOwner,
        ShowInTaskbar = false,
        Icon = _owner.Icon
    };

    private static StackPanel CreateDialogContent(
        string heading,
        string message,
        params Control[] buttons)
    {
        var buttonPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Spacing = 8
        };
        foreach (var button in buttons)
            buttonPanel.Children.Add(button);

        return new StackPanel
        {
            Margin = new Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock
                {
                    Text = heading,
                    FontSize = 20,
                    FontWeight = Avalonia.Media.FontWeight.SemiBold
                },
                new TextBlock
                {
                    Text = message,
                    TextWrapping = Avalonia.Media.TextWrapping.Wrap
                },
                buttonPanel
            }
        };
    }
}
