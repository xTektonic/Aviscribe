using System.Text.Json;

namespace Aviscribe.Core.Online;

public sealed class OnlineResumeRecord
{
    public string ServerAddress { get; set; } = string.Empty;
    public int ServerPort { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public Guid SessionId { get; set; }
    public int Generation { get; set; }
    public long Revision { get; set; }
    public Guid ParticipantId { get; set; }
    public string ParticipantToken { get; set; } = string.Empty;
    public string JoinCode { get; set; } = string.Empty;
    public List<PersistedOutboxEvent> Outbox { get; set; } = [];
    public List<WireMoonKeyDto> LocallyOwnedPendingMoons { get; set; } = [];
}

public sealed class PersistedOutboxEvent
{
    public Guid SessionId { get; set; }
    public int Generation { get; set; }
    public WireRunEvent Event { get; set; } = new();
}

public sealed class OnlineResumeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(OnlineProtocol.JsonOptions)
    {
        WriteIndented = true
    };

    public OnlineResumeRecord? Load(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return JsonSerializer.Deserialize<OnlineResumeRecord>(stream, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public void Save(string path, OnlineResumeRecord record)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(record, JsonOptions));
            File.Move(temporary, path, true);
            RestrictPermissions(path);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    public void Delete(string path)
    {
        if (File.Exists(path)) File.Delete(path);
    }

    private static void RestrictPermissions(string path)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS()) return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
            // Best effort on filesystems that do not expose Unix permissions.
        }
    }
}
