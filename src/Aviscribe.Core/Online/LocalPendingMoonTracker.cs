namespace Aviscribe.Core.Online;

internal sealed class LocalPendingMoonTracker
{
    private readonly HashSet<WireMoonKey> _moons = [];

    public bool Contains(WireMoonKey moon) => _moons.Contains(moon);

    public void Clear() => _moons.Clear();

    public void Restore(IEnumerable<WireMoonKey> moons)
    {
        _moons.Clear();
        _moons.UnionWith(moons);
    }

    public void Apply(WireMoonKey moon, RunEventKind kind, bool addedByLocalParticipant)
    {
        if (kind is RunEventKind.HintObserved or RunEventKind.SetPending)
        {
            if (addedByLocalParticipant)
                _moons.Add(moon);
            else
                _moons.Remove(moon);
            return;
        }

        _moons.Remove(moon);
    }

    public void Reconcile(
        IEnumerable<OnlineMoonFact> facts,
        IEnumerable<OnlineFeedItem> recentEvents,
        Guid participantId,
        bool generationChanged)
    {
        if (generationChanged)
            _moons.Clear();

        foreach (var item in recentEvents.OrderBy(item => item.Revision))
        {
            if (item.Moon == null ||
                !Enum.TryParse<RunEventKind>(item.Kind, out var kind))
                continue;

            Apply(item.Moon.ToKey(), kind, item.ActorParticipantId == participantId);
        }

        var pending = facts
            .Where(item => item.Hinted && !item.Collected)
            .Select(item => item.Moon.ToKey())
            .ToHashSet();
        _moons.IntersectWith(pending);
    }

    public IReadOnlyList<WireMoonKeyDto> CreateSnapshot() => _moons
        .OrderBy(item => item.KingdomId)
        .ThenBy(item => item.MoonId)
        .Select(item => new WireMoonKeyDto
        {
            KingdomId = item.KingdomId,
            MoonId = item.MoonId
        })
        .ToList();
}
