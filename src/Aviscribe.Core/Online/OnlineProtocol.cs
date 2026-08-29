using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aviscribe.Core.Online;

public static class OnlineProtocol
{
    public const int Version = 1;
    public const int MaximumRequestSize = 64 * 1024;
    public const int MaximumResponseSize = 4 * 1024 * 1024;
    public static readonly byte[] Magic =
    [
        (byte)'A', (byte)'V', (byte)'I', (byte)'S', (byte)'C',
        (byte)'R', (byte)'I', (byte)'B', (byte)'E', (byte)'_',
        (byte)'A', (byte)'P', (byte)'I', (byte)'_', (byte)'V',
        (byte)'1', 0, 0, 0, 0
    ];

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

public sealed class OnlineRequest
{
    public int Version { get; set; } = OnlineProtocol.Version;
    public Guid RequestId { get; set; } = Guid.NewGuid();
    public string Operation { get; set; } = string.Empty;
    public Guid? SessionId { get; set; }
    public Guid? ParticipantId { get; set; }
    public string? ParticipantToken { get; set; }
    public object? Data { get; set; }
}

public sealed class OnlineResponse
{
    public int Version { get; set; }
    public Guid RequestId { get; set; }
    public bool Ok { get; set; }
    public JsonElement Data { get; set; }
    public OnlineError? Error { get; set; }
}

public sealed record OnlineError(string Code, string Message);

public sealed class OnlineApiException : Exception
{
    public OnlineApiException(string code, string message) : base(message) => Code = code;
    public string Code { get; }
}

public sealed class OnlineCapabilities
{
    public bool Enabled { get; set; }
    public List<int> ProtocolVersions { get; set; } = [];
    public int MaximumActiveRuns { get; set; }
    public int MaximumParticipantsPerRun { get; set; }
    public int IdleExpirationMinutes { get; set; }
    public int? MaximumRunHours { get; set; }
    public int WaitTimeoutSeconds { get; set; }
    public int MaximumRequestBytes { get; set; }
    public int MaximumResponseBytes { get; set; }
    public int MaximumEventsPerPublish { get; set; }
    public int MaximumEventsPerRun { get; set; }
    public int RetainedChanges { get; set; }
    public int RetainedFeedItems { get; set; }
}

public sealed class OnlineRunConfiguration
{
    public string Category { get; set; } = "standard";
    public bool IncludePostGame { get; set; }
}

public sealed class OnlineCreateRunData
{
    public string DisplayName { get; set; } = string.Empty;
    public string CatalogHash { get; set; } = string.Empty;
    public OnlineRunConfiguration Configuration { get; set; } = new();
}

public sealed class OnlineJoinRunData
{
    public string DisplayName { get; set; } = string.Empty;
    public string JoinCode { get; set; } = string.Empty;
    public string CatalogHash { get; set; } = string.Empty;
}

public sealed class OnlineResumeRunData
{
    public string JoinCode { get; set; } = string.Empty;
}

public sealed class OnlinePublishData
{
    public int Generation { get; set; }
    public long BaseRevision { get; set; }
    public List<WireRunEvent> Events { get; set; } = [];
}

public sealed class OnlineWaitData
{
    public int Generation { get; set; }
    public long AfterRevision { get; set; }
}

public sealed class OnlineResetData
{
    public OnlineRunConfiguration Configuration { get; set; } = new();
}

public sealed class WireRunEvent
{
    [JsonPropertyName("id")]
    public Guid EventId { get; set; }
    [JsonPropertyName("t")]
    public RunEventKind Kind { get; set; }
    [JsonPropertyName("k")]
    public int KingdomId { get; set; }
    [JsonPropertyName("m")]
    public int MoonId { get; set; }

    public SharedRunEvent ToShared() => new(
        EventId,
        Kind,
        new WireMoonKey(KingdomId, MoonId));
    public static WireRunEvent FromShared(SharedRunEvent value) => new()
    {
        EventId = value.EventId,
        Kind = value.Kind,
        KingdomId = value.Moon.KingdomId,
        MoonId = value.Moon.MoonId
    };
}

public sealed class WireMoonKeyDto
{
    [JsonPropertyName("k")]
    public int KingdomId { get; set; }
    [JsonPropertyName("m")]
    public int MoonId { get; set; }
    public WireMoonKey ToKey() => new(KingdomId, MoonId);
}

public sealed class OnlineMoonFact
{
    [JsonPropertyName("moon")]
    public WireMoonKeyDto Moon { get; set; } = new();
    [JsonPropertyName("h")]
    public bool Hinted { get; set; }
    [JsonPropertyName("c")]
    public bool Collected { get; set; }
    [JsonPropertyName("x")]
    public ManualClassification ManualClassification { get; set; }
}

public sealed class OnlineParticipant
{
    public Guid ParticipantId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public bool IsOnline { get; set; }
    public bool IsOwner { get; set; }
    public long JoinedSequence { get; set; }
}

public sealed class OnlineFeedItem
{
    public long Revision { get; set; }
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? ActorParticipantId { get; set; }
    public string ActorDisplayName { get; set; } = string.Empty;
    public WireMoonKeyDto? Moon { get; set; }
    public string Message { get; set; } = string.Empty;
}

public sealed class OnlineRunSnapshot
{
    public Guid SessionId { get; set; }
    public int Generation { get; set; }
    public long Revision { get; set; }
    public OnlineRunConfiguration Configuration { get; set; } = new();
    public Guid? OwnerParticipantId { get; set; }
    public List<OnlineMoonFact> MoonFacts { get; set; } = [];
    public List<OnlineParticipant> Participants { get; set; } = [];
    public List<OnlineFeedItem> RecentEvents { get; set; } = [];
}

public sealed class OnlineConnectionResult
{
    public Guid SessionId { get; set; }
    public int Generation { get; set; }
    public string? JoinCode { get; set; }
    public Guid ParticipantId { get; set; }
    public string ParticipantToken { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public OnlineRunSnapshot Snapshot { get; set; } = new();
}

public sealed class OnlineEventReceipt
{
    [JsonPropertyName("id")]
    public Guid EventId { get; set; }
    [JsonPropertyName("r")]
    public long Revision { get; set; }
    [JsonPropertyName("d")]
    public bool WasDuplicate { get; set; }
}

public sealed class OnlinePublishResult
{
    public int Generation { get; set; }
    public long Revision { get; set; }
    public List<OnlineEventReceipt> Events { get; set; } = [];
}

public sealed class OnlineRunChange
{
    public long Revision { get; set; }
    public string Kind { get; set; } = string.Empty;
    public Guid? ActorParticipantId { get; set; }
    public string ActorDisplayName { get; set; } = string.Empty;
    public WireRunEvent? Event { get; set; }
    public Guid? OwnerParticipantId { get; set; }
    public OnlineParticipant? Participant { get; set; }
    public int? Generation { get; set; }
}

public sealed class OnlineWaitResult
{
    public string Kind { get; set; } = "heartbeat";
    public int Generation { get; set; }
    public long Revision { get; set; }
    public List<OnlineRunChange>? Changes { get; set; }
    public OnlineRunSnapshot? Snapshot { get; set; }
}
