using System.Security.Cryptography;
using System.Text;

namespace Aviscribe.Core.Online;

public enum RunEventKind
{
    HintObserved = 0,
    CollectionObserved = 1,
    SetPending = 2,
    SetCounted = 3,
    SetUncounted = 4,
    RemoveMoon = 5
}

public enum ManualClassification
{
    Automatic = 0,
    Counted = 1,
    Uncounted = 2
}

public readonly record struct MoonFactKey(string Kingdom, int MoonId)
{
    public static MoonFactKey FromMoon(Moon moon) => new(moon.Kingdom, moon.Id);
}

public readonly record struct WireMoonKey(int KingdomId, int MoonId);

public readonly record struct RunFact(
    bool Hinted,
    bool Collected,
    ManualClassification ManualClassification = ManualClassification.Automatic);

public sealed record RunFactSnapshot(
    string Kingdom,
    int MoonId,
    bool Hinted,
    bool Collected,
    ManualClassification ManualClassification);

public sealed record SharedRunEvent(
    Guid EventId,
    RunEventKind Kind,
    WireMoonKey Moon,
    bool IsAutomaticCaptureEvent = false);

public static class RunFactReducer
{
    public static RunFact? Apply(RunFact? current, RunEventKind kind)
    {
        var fact = current ?? new RunFact();
        return kind switch
        {
            RunEventKind.HintObserved => fact with { Hinted = true },
            RunEventKind.CollectionObserved => fact with { Collected = true },
            RunEventKind.SetPending => new RunFact(
                Hinted: true,
                Collected: false,
                ManualClassification.Automatic),
            RunEventKind.SetCounted => fact with
            {
                Collected = true,
                ManualClassification = ManualClassification.Counted
            },
            RunEventKind.SetUncounted => fact with
            {
                Collected = true,
                ManualClassification = ManualClassification.Uncounted
            },
            RunEventKind.RemoveMoon => null,
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
        };
    }
}

public sealed class OnlineCatalog
{
    private readonly Dictionary<string, int> _kingdomIds;
    private readonly Dictionary<(int KingdomId, int MoonId), Moon> _moons;

    public OnlineCatalog(MoonRepository repository)
    {
        var kingdoms = repository.Moons
            .Select(moon => Normalize(moon.Kingdom))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        _kingdomIds = kingdoms
            .Select((name, id) => (name, id))
            .ToDictionary(item => item.name, item => item.id, StringComparer.Ordinal);
        _moons = repository.Moons.ToDictionary(
            moon => (GetKingdomId(moon.Kingdom), moon.Id),
            moon => moon);
        Hash = CalculateHash(repository.Moons);
    }

    public string Hash { get; }

    public int GetKingdomId(string kingdom) => _kingdomIds.TryGetValue(Normalize(kingdom), out var id)
        ? id
        : throw new KeyNotFoundException($"Kingdom '{kingdom}' is not in the online catalog.");

    public WireMoonKey ToWire(Moon moon) => new(GetKingdomId(moon.Kingdom), moon.Id);

    public Moon? Resolve(WireMoonKey key) => _moons.GetValueOrDefault((key.KingdomId, key.MoonId));

    public static string CalculateHash(IEnumerable<Moon> moons)
    {
        var normalized = moons
            .Select(moon => string.Join('|',
                Normalize(moon.Kingdom),
                moon.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                Normalize(moon.CollectionKingdom ?? moon.Kingdom),
                moon.IsStory ? "1" : "0",
                moon.IsMulti ? "1" : "0"))
            .OrderBy(value => value, StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(
            Encoding.UTF8.GetBytes(string.Join('\n', normalized))));
    }

    private static string Normalize(string value) => value.Trim().ToLowerInvariant();
}
