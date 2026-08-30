using Avalonia;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using Aviscribe.Core;
using Aviscribe.Core.Online;

namespace Aviscribe.UI;

public partial class OnlineRunWindow : Window
{
    private static readonly IBrush ConnectedBrush = new SolidColorBrush(Color.Parse("#2E9B68"));
    private static readonly IBrush WarningBrush = new SolidColorBrush(Color.Parse("#D99227"));
    private static readonly IBrush DisconnectedBrush = new SolidColorBrush(Color.Parse("#C65353"));
    private readonly OnlineRunCoordinator _online = null!;
    private readonly AppPreferences _preferences = null!;
    private readonly Action _savePreferences = null!;
    private readonly Func<bool> _hasLocalState = null!;
    private readonly Func<Task<bool>> _confirmReplace = null!;
    private readonly Func<RunSettings> _settings = null!;
    private bool _busy;

    public OnlineRunWindow()
    {
        InitializeComponent();
    }

    public OnlineRunWindow(
        OnlineRunCoordinator online,
        AppPreferences preferences,
        Action savePreferences,
        Func<bool> hasLocalState,
        Func<Task<bool>> confirmReplace,
        Func<RunSettings> settings)
        : this()
    {
        _online = online;
        _preferences = preferences;
        _savePreferences = savePreferences;
        _hasLocalState = hasLocalState;
        _confirmReplace = confirmReplace;
        _settings = settings;

        this.GetControl<TextBox>("txtServerAddress").Text = preferences.OnlineServerAddress;
        this.GetControl<TextBox>("txtServerPort").Text = preferences.OnlineServerPort.ToString();
        this.GetControl<TextBox>("txtDisplayName").Text = preferences.OnlineDisplayName;
        this.GetControl<ComboBox>("cbOnlineCategory").ItemsSource = Enum.GetValues<RunCategory>();
        this.GetControl<ComboBox>("cbOnlineCategory").SelectedItem = settings().Category;
        this.GetControl<CheckBox>("chkOnlinePostgame").IsChecked = settings().IncludePostGameKingdoms;

        this.GetControl<Button>("btnClose").Click += (_, _) => Close();
        this.GetControl<Button>("btnCreateRun").Click += CreateRun;
        this.GetControl<Button>("btnJoinRun").Click += JoinRun;
        this.GetControl<Button>("btnResumeRun").Click += ResumeRun;
        this.GetControl<Button>("btnOnlineLeave").Click += LeaveRun;
        this.GetControl<Button>("btnOnlineReset").Click += ResetRun;
        this.GetControl<Button>("btnOnlineEnd").Click += EndRun;
        this.GetControl<Button>("btnCopyJoinCode").Click += CopyJoinCode;
        _online.StateChanged += OnlineStateChanged;
        Closed += (_, _) => _online.StateChanged -= OnlineStateChanged;
        Refresh();
    }

    private async void CreateRun(object? sender, RoutedEventArgs args)
    {
        if (!await ValidateAndConfirmAsync(alwaysConfirmNonEmpty: true)) return;
        await RunBusyAsync(async token =>
        {
            var endpoint = SaveEndpointPreferences();
            await _online.CreateAsync(endpoint.Address, endpoint.Port, endpoint.DisplayName, _settings(), token);
        });
    }

    private async void JoinRun(object? sender, RoutedEventArgs args)
    {
        if (!await ValidateAndConfirmAsync(alwaysConfirmNonEmpty: true)) return;
        var code = this.GetControl<TextBox>("txtJoinCode").Text ?? string.Empty;
        if (code.Trim().Replace("-", string.Empty).Length != 8)
        {
            ShowMessage("Enter the eight-character join code.");
            return;
        }
        await RunBusyAsync(async token =>
        {
            var endpoint = SaveEndpointPreferences();
            await _online.JoinAsync(endpoint.Address, endpoint.Port, endpoint.DisplayName, code, token);
        });
    }

    private async void ResumeRun(object? sender, RoutedEventArgs args)
    {
        if (!await ValidateAndConfirmAsync(alwaysConfirmNonEmpty: true, validateFields: false)) return;
        await RunBusyAsync(_online.ResumePreviousAsync);
    }

    private async void LeaveRun(object? sender, RoutedEventArgs args)
    {
        if (!await ConfirmAsync(
                "Leave Multiplayer Room?",
                "You will leave the room and the saved rejoin credential will be deleted.",
                "Leave Room")) return;
        await RunBusyAsync(_online.LeaveAsync);
    }

    private async void ResetRun(object? sender, RoutedEventArgs args)
    {
        if (!await ConfirmAsync(
                "Start a New Run?",
                "This clears shared moon state for every player in the room and starts a new run.",
                "Start New Run")) return;
        var settings = _settings().Clone();
        if (this.GetControl<ComboBox>("cbOnlineCategory").SelectedItem is RunCategory category)
            settings.Category = category;
        settings.IncludePostGameKingdoms = this.GetControl<CheckBox>("chkOnlinePostgame").IsChecked == true;
        await RunBusyAsync(token => _online.ResetAsync(settings, token));
    }

    private async void EndRun(object? sender, RoutedEventArgs args)
    {
        if (!await ConfirmAsync(
                "End Multiplayer Run?",
                "This ends the run and closes the room for every player. It cannot be resumed.",
                "End Run")) return;
        await RunBusyAsync(_online.EndAsync);
    }

    private async void CopyJoinCode(object? sender, RoutedEventArgs args)
    {
        var code = _online.JoinCode;
        if (string.IsNullOrWhiteSpace(code)) return;
        try
        {
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                ShowMessage("Clipboard access is unavailable. Select the room code and copy it manually.");
                return;
            }
            await clipboard.SetTextAsync(code);
            ShowMessage("Room join code copied to the clipboard.");
        }
        catch (Exception ex)
        {
            ShowMessage($"Could not copy the room code: {ex.Message}. You can still select and copy it manually.");
        }
    }

    private async Task<bool> ValidateAndConfirmAsync(bool alwaysConfirmNonEmpty, bool validateFields = true)
    {
        if (_busy) return false;
        if (validateFields)
        {
            var address = this.GetControl<TextBox>("txtServerAddress").Text;
            var name = this.GetControl<TextBox>("txtDisplayName").Text;
            if (string.IsNullOrWhiteSpace(address) || string.IsNullOrWhiteSpace(name) ||
                !int.TryParse(this.GetControl<TextBox>("txtServerPort").Text, out var port) || port is < 1 or > 65535)
            {
                ShowMessage("Enter a server address, valid port, and display name.");
                return false;
            }
        }
        return !alwaysConfirmNonEmpty || !_hasLocalState() || await _confirmReplace();
    }

    private (string Address, int Port, string DisplayName) SaveEndpointPreferences()
    {
        var address = this.GetControl<TextBox>("txtServerAddress").Text!.Trim();
        var port = int.Parse(this.GetControl<TextBox>("txtServerPort").Text!);
        var name = this.GetControl<TextBox>("txtDisplayName").Text!.Trim();
        _preferences.OnlineServerAddress = address;
        _preferences.OnlineServerPort = port;
        _preferences.OnlineDisplayName = name;
        _savePreferences();
        return (address, port, name);
    }

    private async Task RunBusyAsync(Func<CancellationToken, Task> action)
    {
        if (_busy) return;
        _busy = true;
        Refresh();
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await action(timeout.Token);
        }
        catch (Exception ex)
        {
            ShowMessage(ex.Message);
        }
        finally
        {
            _busy = false;
            Refresh();
        }
    }

    private void OnlineStateChanged(object? sender, EventArgs args) =>
        Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        var joined = _online.IsJoined;
        this.GetControl<Control>("pnlDisconnected").IsVisible = !joined;
        this.GetControl<Control>("pnlConnected").IsVisible = joined;
        this.GetControl<Button>("btnOnlineLeave").IsVisible = joined;
        this.GetControl<TextBlock>("txtOnlineStatus").Text = _online.State switch
        {
            OnlineConnectionState.Connected => $"Room connected · {_online.Participants.Count(item => item.IsOnline)} players online",
            OnlineConnectionState.Reconnecting => "Reconnecting",
            OnlineConnectionState.SharingPaused => "Room connected · sharing paused",
            _ => "Not in a room"
        };
        SetStatusDot(this.GetControl<Border>("multiplayerStatusDot"), _online.State);
        if (!string.IsNullOrWhiteSpace(_online.LastMessage))
            this.GetControl<TextBlock>("txtOnlineMessage").Text = _online.LastMessage;
        this.GetControl<Button>("btnResumeRun").IsVisible = _online.HasPreviousRun;
        foreach (var name in new[] { "btnCreateRun", "btnJoinRun", "btnResumeRun", "btnOnlineLeave", "btnOnlineReset", "btnOnlineEnd", "btnCopyJoinCode" })
            this.GetControl<Button>(name).IsEnabled = !_busy;
        if (!joined) return;

        this.GetControl<TextBox>("txtConnectedCode").Text = string.IsNullOrWhiteSpace(_online.JoinCode) ? "—" : _online.JoinCode;
        this.GetControl<Button>("btnCopyJoinCode").IsEnabled = !_busy && !string.IsNullOrWhiteSpace(_online.JoinCode);
        this.GetControl<TextBlock>("txtCaptureSharing").Text = _online.CaptureSharingArmed
            ? _online.State switch
            {
                OnlineConnectionState.Connected => "Sharing active",
                OnlineConnectionState.Reconnecting => "Queued · reconnecting",
                _ => "Sharing paused"
            }
            : "Paused · start capture";
        SetStatusDot(
            this.GetControl<Border>("captureSharingDot"),
            !_online.CaptureSharingArmed
                ? "disconnected"
                : _online.State == OnlineConnectionState.Connected
                    ? "connected"
                    : "warning");
        var owner = _online.Participants.FirstOrDefault(item => item.ParticipantId == _online.OwnerParticipantId);
        this.GetControl<TextBlock>("txtOwner").Text = owner?.DisplayName ?? "Vacant";
        this.GetControl<ListBox>("lstOnlineParticipants").ItemsSource = _online.Participants.Select(item =>
        {
            var isCurrentPlayerWaiting = item.ParticipantId == _online.ParticipantId &&
                                         _online.State is OnlineConnectionState.Reconnecting or
                                             OnlineConnectionState.SharingPaused;
            var status = isCurrentPlayerWaiting ? "Connecting" : item.IsOnline ? "Online" : "Offline";
            var brush = isCurrentPlayerWaiting ? WarningBrush : item.IsOnline ? ConnectedBrush : DisconnectedBrush;
            var role = item.ParticipantId == _online.OwnerParticipantId ? " · Owner" : string.Empty;
            return new PlayerListItem(item.DisplayName, status + role, brush);
        }).ToList();
        this.GetControl<ListBox>("lstOnlineEvents").ItemsSource = _online.RecentEvents
            .OrderByDescending(item => item.Revision)
            .Select(_online.DescribeFeedItem)
            .ToList();
        this.GetControl<Border>("pnlOwnerControls").IsVisible = _online.IsOwner;
    }

    private static void SetStatusDot(Border dot, OnlineConnectionState state)
        => SetStatusDot(dot, state switch
        {
            OnlineConnectionState.Connected => "connected",
            OnlineConnectionState.Reconnecting or OnlineConnectionState.SharingPaused => "warning",
            _ => "disconnected"
        });

    private static void SetStatusDot(Border dot, string statusClass)
    {
        dot.Classes.Remove("connected");
        dot.Classes.Remove("warning");
        dot.Classes.Remove("disconnected");
        dot.Classes.Add(statusClass);
    }

    private void ShowMessage(string message) =>
        this.GetControl<TextBlock>("txtOnlineMessage").Text = message;

    private async Task<bool> ConfirmAsync(string title, string message, string action)
    {
        var confirmation = new Window
        {
            Title = title,
            Width = 450,
            Height = 205,
            CanResize = false,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            ShowInTaskbar = false,
            Icon = Icon
        };
        var confirm = new Button { Content = action };
        confirm.Classes.Add("danger");
        var cancel = new Button { Content = "Cancel" };
        confirm.Click += (_, _) => confirmation.Close(true);
        cancel.Click += (_, _) => confirmation.Close(false);
        confirmation.Content = new StackPanel
        {
            Margin = new Avalonia.Thickness(24),
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = title, FontSize = 20, FontWeight = Avalonia.Media.FontWeight.SemiBold },
                new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                new StackPanel
                {
                    Orientation = Avalonia.Layout.Orientation.Horizontal,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                    Spacing = 8,
                    Children = { cancel, confirm }
                }
            }
        };
        return await confirmation.ShowDialog<bool>(this);
    }

    private sealed record PlayerListItem(string DisplayName, string Detail, IBrush StatusBrush);
}
