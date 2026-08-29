using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Aviscribe.Core;
using Aviscribe.Core.Online;

namespace Aviscribe.UI;

public partial class OnlineRunWindow : Window
{
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
                "Leave Online Run?",
                "You will be removed from the run and the saved resume credential will be deleted.",
                "Leave Run")) return;
        await RunBusyAsync(_online.LeaveAsync);
    }

    private async void ResetRun(object? sender, RoutedEventArgs args)
    {
        if (!await ConfirmAsync(
                "Reset Online Run?",
                "This clears shared moon state for every participant and starts a new generation.",
                "Reset Run")) return;
        var settings = _settings().Clone();
        if (this.GetControl<ComboBox>("cbOnlineCategory").SelectedItem is RunCategory category)
            settings.Category = category;
        settings.IncludePostGameKingdoms = this.GetControl<CheckBox>("chkOnlinePostgame").IsChecked == true;
        await RunBusyAsync(token => _online.ResetAsync(settings, token));
    }

    private async void EndRun(object? sender, RoutedEventArgs args)
    {
        if (!await ConfirmAsync(
                "End Online Run?",
                "This ends the session for every participant. It cannot be resumed.",
                "End Run")) return;
        await RunBusyAsync(_online.EndAsync);
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
        this.GetControl<StackPanel>("pnlDisconnected").IsVisible = !joined;
        this.GetControl<StackPanel>("pnlConnected").IsVisible = joined;
        this.GetControl<TextBlock>("txtOnlineStatus").Text = _online.State switch
        {
            OnlineConnectionState.Connected => $"Online · {_online.Participants.Count(item => item.IsOnline)} players",
            OnlineConnectionState.Reconnecting => "Reconnecting",
            OnlineConnectionState.SharingPaused => "Online · sharing paused",
            _ => "Offline"
        };
        if (!string.IsNullOrWhiteSpace(_online.LastMessage))
            this.GetControl<TextBlock>("txtOnlineMessage").Text = _online.LastMessage;
        this.GetControl<Button>("btnResumeRun").IsVisible = _online.HasPreviousRun;
        foreach (var name in new[] { "btnCreateRun", "btnJoinRun", "btnResumeRun", "btnOnlineLeave", "btnOnlineReset", "btnOnlineEnd" })
            this.GetControl<Button>(name).IsEnabled = !_busy;
        if (!joined) return;

        this.GetControl<TextBlock>("txtConnectedCode").Text = string.IsNullOrWhiteSpace(_online.JoinCode) ? "—" : _online.JoinCode;
        this.GetControl<TextBlock>("txtCaptureSharing").Text = _online.CaptureSharingArmed
            ? "Active while capture runs"
            : "Automatic detections paused";
        var owner = _online.Participants.FirstOrDefault(item => item.ParticipantId == _online.OwnerParticipantId);
        this.GetControl<TextBlock>("txtOwner").Text = owner?.DisplayName ?? "Vacant";
        this.GetControl<ListBox>("lstOnlineParticipants").ItemsSource = _online.Participants.Select(item =>
            $"{item.DisplayName} · {(item.IsOnline ? "online" : "offline")}" +
            (item.ParticipantId == _online.OwnerParticipantId ? " · owner" : string.Empty)).ToList();
        this.GetControl<ListBox>("lstOnlineEvents").ItemsSource = _online.RecentEvents
            .OrderByDescending(item => item.Revision)
            .Select(item => item.Message)
            .ToList();
        this.GetControl<Border>("pnlOwnerControls").IsVisible = _online.IsOwner;
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
            ShowInTaskbar = false
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
}
