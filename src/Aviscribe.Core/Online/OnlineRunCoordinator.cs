using System.Text.Json;

namespace Aviscribe.Core.Online;

public enum OnlineConnectionState
{
    Offline,
    Connected,
    Reconnecting,
    SharingPaused
}

public sealed class OnlineRunCoordinator : IAsyncDisposable
{
    private static readonly TimeSpan[] RetryDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];
    private readonly object _sync = new();
    private readonly object _persistenceSync = new();
    private readonly RunCoordinator _runs;
    private readonly OnlineResumeStore _resumeStore;
    private readonly string _resumePath;
    private readonly SemaphoreSlim _publishSignal = new(0, 1);
    private readonly List<PersistedOutboxEvent> _outbox = [];
    private CancellationTokenSource? _sessionCancellation;
    private Task? _sessionTask;
    private OnlineApiClient? _api;
    private OnlineResumeRecord? _credentials;
    private int _retryIndex;
    private bool _captureSharingArmed;
    private bool _sharingPaused;

    public OnlineRunCoordinator(
        RunCoordinator runs,
        OnlineResumeStore? resumeStore = null,
        string? resumePath = null)
    {
        _runs = runs;
        _resumeStore = resumeStore ?? new OnlineResumeStore();
        _resumePath = resumePath ?? AppPaths.OnlineResumePath;
        _runs.LocalEventCreated += OnLocalEventCreated;
    }

    public event EventHandler? StateChanged;
    public OnlineConnectionState State { get; private set; } = OnlineConnectionState.Offline;
    public bool CaptureSharingArmed
    {
        get => _captureSharingArmed;
        set
        {
            if (_captureSharingArmed == value) return;
            _captureSharingArmed = value;
            RaiseStateChanged();
        }
    }
    public string? LastMessage { get; private set; }
    public Guid? SessionId => _credentials?.SessionId;
    public int Generation => _credentials?.Generation ?? 0;
    public long Revision => _credentials?.Revision ?? 0;
    public Guid? ParticipantId => _credentials?.ParticipantId;
    public string JoinCode => _credentials?.JoinCode ?? string.Empty;
    public Guid? OwnerParticipantId { get; private set; }
    public IReadOnlyList<OnlineParticipant> Participants { get; private set; } = [];
    public IReadOnlyList<OnlineFeedItem> RecentEvents { get; private set; } = [];
    public bool IsJoined => _credentials != null && _sessionCancellation is { IsCancellationRequested: false };
    public bool IsOwner => ParticipantId.HasValue && ParticipantId == OwnerParticipantId;
    public bool HasPreviousRun => _resumeStore.Load(_resumePath) != null;

    public async Task<OnlineCapabilities> ProbeAsync(
        string address,
        int port,
        CancellationToken cancellationToken) =>
        await new OnlineApiClient(address, port).GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);

    public async Task CreateAsync(
        string address,
        int port,
        string displayName,
        RunSettings settings,
        CancellationToken cancellationToken)
    {
        await StopSessionAsync().ConfigureAwait(false);
        var api = new OnlineApiClient(address, port);
        await ValidateCapabilitiesAsync(api, cancellationToken).ConfigureAwait(false);
        var result = await api.SendAsync<OnlineConnectionResult>(new OnlineRequest
        {
            Operation = "createRun",
            Data = new OnlineCreateRunData
            {
                DisplayName = displayName,
                CatalogHash = _runs.Catalog.Hash,
                Configuration = Configuration(settings)
            }
        }, cancellationToken).ConfigureAwait(false);
        lock (_sync) _outbox.Clear();
        _runs.ResetLocal();
        BeginSession(api, result, address, port, displayName, result.JoinCode ?? string.Empty);
    }

    public async Task JoinAsync(
        string address,
        int port,
        string displayName,
        string joinCode,
        CancellationToken cancellationToken)
    {
        await StopSessionAsync().ConfigureAwait(false);
        var api = new OnlineApiClient(address, port);
        await ValidateCapabilitiesAsync(api, cancellationToken).ConfigureAwait(false);
        var result = await api.SendAsync<OnlineConnectionResult>(new OnlineRequest
        {
            Operation = "joinRun",
            Data = new OnlineJoinRunData
            {
                DisplayName = displayName,
                JoinCode = joinCode,
                CatalogHash = _runs.Catalog.Hash
            }
        }, cancellationToken).ConfigureAwait(false);
        lock (_sync) _outbox.Clear();
        BeginSession(api, result, address, port, displayName, FormatJoinCode(joinCode));
    }

    public async Task ResumePreviousAsync(CancellationToken cancellationToken)
    {
        await StopSessionAsync().ConfigureAwait(false);
        var saved = _resumeStore.Load(_resumePath) ??
                    throw new OnlineApiException("runNotFound", "There is no previous online run to resume.");
        var api = new OnlineApiClient(saved.ServerAddress, saved.ServerPort);
        await ValidateCapabilitiesAsync(api, cancellationToken).ConfigureAwait(false);
        var result = await api.SendAsync<OnlineConnectionResult>(AuthenticatedRequest(saved, "resumeRun"), cancellationToken)
            .ConfigureAwait(false);
        lock (_sync)
        {
            _outbox.Clear();
            _outbox.AddRange(saved.Outbox.Where(item =>
                item.SessionId == result.SessionId && item.Generation == result.Generation));
        }
        BeginSession(api, result, saved.ServerAddress, saved.ServerPort, saved.DisplayName, saved.JoinCode);
    }

    public async Task LeaveAsync(CancellationToken cancellationToken)
    {
        var (api, credentials) = RequireSession();
        await api.SendAsync<JsonElement>(AuthenticatedRequest(credentials, "leaveRun"), cancellationToken)
            .ConfigureAwait(false);
        await StopSessionAsync().ConfigureAwait(false);
        DeleteResume();
        lock (_sync) _outbox.Clear();
        _credentials = null;
        _api = null;
        SetState(OnlineConnectionState.Offline, "Left the online run.");
    }

    public async Task ResetAsync(RunSettings settings, CancellationToken cancellationToken)
    {
        var (api, credentials) = RequireSession();
        var snapshot = await api.SendAsync<OnlineRunSnapshot>(AuthenticatedRequest(
            credentials,
            "resetRun",
            new OnlineResetData { Configuration = Configuration(settings) }), cancellationToken).ConfigureAwait(false);
        lock (_sync) _outbox.Clear();
        ApplySnapshot(snapshot, generationChanged: true);
    }

    public async Task EndAsync(CancellationToken cancellationToken)
    {
        var (api, credentials) = RequireSession();
        await api.SendAsync<JsonElement>(AuthenticatedRequest(credentials, "endRun"), cancellationToken)
            .ConfigureAwait(false);
        await EndedAsync("The online run ended.").ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _runs.LocalEventCreated -= OnLocalEventCreated;
        await StopSessionAsync().ConfigureAwait(false);
        _publishSignal.Dispose();
    }

    private void BeginSession(
        OnlineApiClient api,
        OnlineConnectionResult result,
        string address,
        int port,
        string displayName,
        string joinCode)
    {
        _api = api;
        _sharingPaused = false;
        _credentials = new OnlineResumeRecord
        {
            ServerAddress = address,
            ServerPort = port,
            DisplayName = displayName,
            SessionId = result.SessionId,
            Generation = result.Generation,
            Revision = result.Snapshot.Revision,
            ParticipantId = result.ParticipantId,
            ParticipantToken = result.ParticipantToken,
            JoinCode = joinCode
        };
        ApplySnapshot(result.Snapshot, generationChanged: false);
        PersistResume();
        _sessionCancellation = new CancellationTokenSource();
        _sessionTask = Task.WhenAll(
            WaitLoopAsync(_sessionCancellation.Token),
            PublishLoopAsync(_sessionCancellation.Token));
        if (_outbox.Count > 0) SignalPublisher();
        SetState(OnlineConnectionState.Connected, "Connected to the online run.");
    }

    private async Task WaitLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var (api, credentials) = RequireSession();
                var result = await api.SendAsync<OnlineWaitResult>(AuthenticatedRequest(
                    credentials,
                    "waitForChanges",
                    new OnlineWaitData
                    {
                        Generation = credentials.Generation,
                        AfterRevision = credentials.Revision
                    }), cancellationToken).ConfigureAwait(false);
                _retryIndex = 0;
                if (result.Kind == "ended")
                {
                    await EndedAsync("The online run ended or expired.").ConfigureAwait(false);
                    return;
                }
                if (result.Snapshot != null)
                    ApplySnapshot(result.Snapshot, result.Generation != credentials.Generation);
                else if (result.Changes != null)
                    ApplyChanges(result);
                else
                {
                    credentials.Revision = result.Revision;
                    PersistResume();
                }
                SetState(OnlineConnectionState.Connected, null);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            catch (OnlineApiException ex) when (ex.Code is "runExpired" or "runNotFound" or "invalidParticipant")
            {
                await EndedAsync(ex.Message).ConfigureAwait(false);
                return;
            }
            catch (Exception ex)
            {
                await RetryAsync(ex.Message, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private async Task PublishLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            await _publishSignal.WaitAsync(cancellationToken).ConfigureAwait(false);
            while (true)
            {
                PersistedOutboxEvent[] batch;
                lock (_sync) batch = _outbox.Take(50).ToArray();
                if (batch.Length == 0) break;
                try
                {
                    var (api, credentials) = RequireSession();
                    batch = batch.Where(item => item.SessionId == credentials.SessionId &&
                                                item.Generation == credentials.Generation).ToArray();
                    if (batch.Length == 0)
                    {
                        lock (_sync) _outbox.RemoveAll(item => item.SessionId != credentials.SessionId ||
                                                               item.Generation != credentials.Generation);
                        PersistResume();
                        break;
                    }
                    var response = await api.SendAsync<OnlinePublishResult>(AuthenticatedRequest(
                        credentials,
                        "publishEvents",
                        new OnlinePublishData
                        {
                            Generation = credentials.Generation,
                            BaseRevision = credentials.Revision,
                            Events = batch.Select(item => item.Event).ToList()
                        }), cancellationToken).ConfigureAwait(false);
                    var accepted = response.Events.Select(item => item.EventId).ToHashSet();
                    lock (_sync) _outbox.RemoveAll(item => accepted.Contains(item.Event.EventId));
                    credentials.Revision = Math.Max(credentials.Revision, response.Revision);
                    PersistResume();
                    SetState(OnlineConnectionState.Connected, null);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (OnlineApiException ex) when (ex.Code == "generationMismatch")
                {
                    await RefreshAuthoritativeSnapshotAsync(cancellationToken).ConfigureAwait(false);
                    break;
                }
                catch (Exception ex)
                {
                    await RetryAsync(ex.Message, cancellationToken).ConfigureAwait(false);
                }
            }
        }
    }

    private async Task RefreshAuthoritativeSnapshotAsync(CancellationToken cancellationToken)
    {
        var (api, credentials) = RequireSession();
        var result = await api.SendAsync<OnlineConnectionResult>(
            AuthenticatedRequest(credentials, "resumeRun"), cancellationToken).ConfigureAwait(false);
        lock (_sync) _outbox.Clear();
        ApplySnapshot(result.Snapshot, generationChanged: true);
        SetState(OnlineConnectionState.Connected, "The run was reset; local queued changes were discarded.");
    }

    private void ApplySnapshot(OnlineRunSnapshot snapshot, bool generationChanged)
    {
        if (_credentials == null) return;
        if (generationChanged || snapshot.Generation != _credentials.Generation)
        {
            lock (_sync) _outbox.Clear();
        }
        _credentials.Generation = snapshot.Generation;
        _credentials.Revision = snapshot.Revision;
        OwnerParticipantId = snapshot.OwnerParticipantId;
        Participants = snapshot.Participants.OrderBy(item => item.JoinedSequence).ToList();
        RecentEvents = snapshot.RecentEvents.TakeLast(200).ToList();
        _runs.ApplySharedConfiguration(
            snapshot.Configuration.Category == "hardcore" ? RunCategory.Hardcore : RunCategory.Standard,
            snapshot.Configuration.IncludePostGame);
        _runs.ReplaceWireFacts(snapshot.MoonFacts.Select(item => (
            item.Moon.ToKey(),
            new RunFact(item.Hinted, item.Collected, item.ManualClassification))));
        PersistResume();
        RaiseStateChanged();
    }

    private void ApplyChanges(OnlineWaitResult result)
    {
        if (_credentials == null) return;
        foreach (var change in result.Changes ?? [])
        {
            if (change.Event != null) _runs.ApplyRemote(change.Event.ToShared());
            if (change.Kind == "participantLeft" && change.ActorParticipantId.HasValue)
                Participants = Participants.Where(item => item.ParticipantId != change.ActorParticipantId).ToList();
            else if (change.Participant != null)
            {
                var participants = Participants.ToList();
                participants.RemoveAll(item => item.ParticipantId == change.Participant.ParticipantId);
                participants.Add(change.Participant);
                Participants = participants.OrderBy(item => item.JoinedSequence).ToList();
            }
            if (change.OwnerParticipantId.HasValue || change.Kind == "ownerChanged")
                OwnerParticipantId = change.OwnerParticipantId;
            var feed = RecentEvents.ToList();
            feed.Add(new OnlineFeedItem
            {
                Revision = change.Revision,
                OccurredAtUtc = DateTimeOffset.UtcNow,
                Kind = change.Event?.Kind.ToString() ?? change.Kind,
                ActorParticipantId = change.ActorParticipantId,
                ActorDisplayName = change.ActorDisplayName,
                Moon = change.Event == null ? null : new WireMoonKeyDto
                {
                    KingdomId = change.Event.KingdomId,
                    MoonId = change.Event.MoonId
                },
                Message = DescribeChange(change)
            });
            RecentEvents = feed.TakeLast(200).ToList();
        }
        _credentials.Revision = result.Revision;
        PersistResume();
        RaiseStateChanged();
    }

    private void OnLocalEventCreated(object? sender, SharedRunEvent runEvent)
    {
        OnlineResumeRecord? credentials;
        lock (_sync) credentials = _credentials;
        if (!IsJoined || credentials == null || _sharingPaused ||
            (runEvent.IsAutomaticCaptureEvent && !CaptureSharingArmed)) return;
        lock (_sync)
        {
            var candidate = new PersistedOutboxEvent
            {
                SessionId = credentials.SessionId,
                Generation = credentials.Generation,
                Event = WireRunEvent.FromShared(runEvent)
            };
            var projectedCount = _outbox.Count + 1;
            var projectedBytes = JsonSerializer.SerializeToUtf8Bytes(
                _outbox.Append(candidate), OnlineProtocol.JsonOptions).Length;
            if (projectedCount > 500 || projectedBytes > 1024 * 1024)
            {
                _sharingPaused = true;
                State = OnlineConnectionState.SharingPaused;
                LastMessage = "Online sharing paused because the retry queue is full. Reconnect or leave the run.";
                RaiseStateChanged();
                return;
            }
            _outbox.Add(candidate);
        }
        PersistResume();
        SignalPublisher();
    }

    private async Task RetryAsync(string message, CancellationToken cancellationToken)
    {
        SetState(OnlineConnectionState.Reconnecting, message);
        var delay = RetryDelays[Math.Min(_retryIndex++, RetryDelays.Length - 1)];
        await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
    }

    private async Task EndedAsync(string message)
    {
        _sessionCancellation?.Cancel();
        DeleteResume();
        lock (_sync) _outbox.Clear();
        _credentials = null;
        _api = null;
        SetState(OnlineConnectionState.Offline, message);
        await Task.CompletedTask;
    }

    private async Task StopSessionAsync()
    {
        var cancellation = _sessionCancellation;
        var task = _sessionTask;
        _sessionCancellation = null;
        _sessionTask = null;
        if (cancellation == null) return;
        cancellation.Cancel();
        try
        {
            if (task != null && Task.CurrentId != task.Id) await task.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            cancellation.Dispose();
        }
    }

    private (OnlineApiClient Api, OnlineResumeRecord Credentials) RequireSession() =>
        (_api ?? throw new OnlineApiException("runNotFound", "No online run is connected."),
         _credentials ?? throw new OnlineApiException("runNotFound", "No online run is connected."));

    private static OnlineRequest AuthenticatedRequest(
        OnlineResumeRecord credentials,
        string operation,
        object? data = null) => new()
    {
        Operation = operation,
        SessionId = credentials.SessionId,
        ParticipantId = credentials.ParticipantId,
        ParticipantToken = credentials.ParticipantToken,
        Data = data
    };

    private static OnlineRunConfiguration Configuration(RunSettings settings) => new()
    {
        Category = settings.Category == RunCategory.Hardcore ? "hardcore" : "standard",
        IncludePostGame = settings.IncludePostGameKingdoms
    };

    private static async Task ValidateCapabilitiesAsync(
        OnlineApiClient api,
        CancellationToken cancellationToken)
    {
        var capabilities = await api.GetCapabilitiesAsync(cancellationToken).ConfigureAwait(false);
        if (!capabilities.ProtocolVersions.Contains(OnlineProtocol.Version))
            throw new OnlineApiException("unsupportedVersion", "The server does not support this Aviscribe protocol version.");
        if (!capabilities.Enabled)
            throw new OnlineApiException("featureDisabled", "Aviscribe online runs are disabled on this server.");
    }

    private void PersistResume()
    {
        if (_credentials == null) return;
        try
        {
            lock (_persistenceSync)
            {
                lock (_sync) _credentials.Outbox = _outbox.ToList();
                _resumeStore.Save(_resumePath, _credentials);
            }
        }
        catch (Exception ex)
        {
            _sharingPaused = true;
            State = OnlineConnectionState.SharingPaused;
            LastMessage = $"Online sharing paused because the resume queue could not be saved: {ex.Message}";
            RaiseStateChanged();
        }
    }

    private void DeleteResume()
    {
        try
        {
            _resumeStore.Delete(_resumePath);
        }
        catch (Exception ex)
        {
            LastMessage = $"The previous-run record could not be removed: {ex.Message}";
        }
    }

    private void SignalPublisher()
    {
        if (_publishSignal.CurrentCount == 0) _publishSignal.Release();
    }

    private void SetState(OnlineConnectionState state, string? message)
    {
        State = state == OnlineConnectionState.Connected && _sharingPaused
            ? OnlineConnectionState.SharingPaused
            : state;
        if (!string.IsNullOrWhiteSpace(message)) LastMessage = message;
        RaiseStateChanged();
    }

    private void RaiseStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);

    private static string FormatJoinCode(string value)
    {
        var normalized = value.Trim().Replace("-", string.Empty).ToUpperInvariant();
        return normalized.Length == 8 ? $"{normalized[..4]}-{normalized[4..]}" : value.Trim().ToUpperInvariant();
    }

    private static string DescribeChange(OnlineRunChange change)
    {
        var actor = string.IsNullOrWhiteSpace(change.ActorDisplayName)
            ? "A participant"
            : change.ActorDisplayName;
        if (change.Event != null) return $"{actor} updated a moon.";
        return change.Kind switch
        {
            "participantJoined" => $"{actor} joined.",
            "participantLeft" => $"{actor} left.",
            "participantOnline" => $"{actor} reconnected.",
            "participantOffline" => $"{actor} disconnected.",
            "ownerChanged" => $"{actor} became the run owner.",
            _ => $"{actor} updated the run."
        };
    }
}
