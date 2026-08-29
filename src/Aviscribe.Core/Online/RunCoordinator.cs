namespace Aviscribe.Core.Online;

public sealed class RunCoordinator
{
    private readonly object _sync = new();
    private readonly GameState _state;
    private readonly MoonRepository _repository;
    private readonly Dictionary<MoonFactKey, RunFact> _facts = new();

    public RunCoordinator(GameState state, MoonRepository repository)
    {
        _state = state;
        _repository = repository;
        Catalog = new OnlineCatalog(repository);
    }

    public OnlineCatalog Catalog { get; }

    public event EventHandler<SharedRunEvent>? LocalEventCreated;

    public IReadOnlyList<RunFactSnapshot> CreateFactSnapshot()
    {
        lock (_sync)
        {
            return _facts
                .OrderBy(item => item.Key.Kingdom, StringComparer.OrdinalIgnoreCase)
                .ThenBy(item => item.Key.MoonId)
                .Select(item => new RunFactSnapshot(
                    item.Key.Kingdom,
                    item.Key.MoonId,
                    item.Value.Hinted,
                    item.Value.Collected,
                    item.Value.ManualClassification))
                .ToList();
        }
    }

    public void ImportLegacyProjection()
    {
        var snapshot = _state.CreateSnapshot();
        lock (_sync)
        {
            _facts.Clear();
            foreach (var moon in snapshot.KingdomStates.Values.SelectMany(value => value.Pending).DistinctBy(Key))
                _facts[Key(moon)] = new RunFact(true, false);
            foreach (var moon in snapshot.KingdomStates.Values.SelectMany(value => value.Collected).DistinctBy(Key))
                _facts[Key(moon)] = new RunFact(false, true, ManualClassification.Counted);
            foreach (var moon in snapshot.KingdomStates.Values.SelectMany(value => value.UncountedCollected).DistinctBy(Key))
                _facts[Key(moon)] = new RunFact(false, true, ManualClassification.Uncounted);
        }
        Project();
    }

    public void ReplaceFacts(IEnumerable<RunFactSnapshot> facts)
    {
        lock (_sync)
        {
            _facts.Clear();
            foreach (var fact in facts)
            {
                if (fact.MoonId < 1 || string.IsNullOrWhiteSpace(fact.Kingdom))
                    continue;
                if (!_repository.Moons.Any(moon => moon.Id == fact.MoonId &&
                        moon.Kingdom.Equals(fact.Kingdom, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!fact.Hinted && !fact.Collected && fact.ManualClassification == ManualClassification.Automatic)
                    continue;
                _facts[new MoonFactKey(fact.Kingdom, fact.MoonId)] = new RunFact(
                    fact.Hinted,
                    fact.Collected,
                    fact.ManualClassification);
            }
        }
        Project();
    }

    public void ReplaceWireFacts(IEnumerable<(WireMoonKey Moon, RunFact Fact)> facts)
    {
        var resolved = facts.Select(item => (Moon: Catalog.Resolve(item.Moon), item.Fact))
            .Where(item => item.Moon != null)
            .Select(item => new RunFactSnapshot(
                item.Moon!.Kingdom,
                item.Moon.Id,
                item.Fact.Hinted,
                item.Fact.Collected,
                item.Fact.ManualClassification));
        ReplaceFacts(resolved);
    }

    public bool ObserveHint(Moon moon, bool automaticCapture = true) =>
        ApplyLocal(RunEventKind.HintObserved, moon, automaticCapture);

    public CollectionOutcome ObserveCollection(Moon moon, bool automaticCapture = true)
    {
        ApplyLocal(RunEventKind.CollectionObserved, moon, automaticCapture);
        lock (_sync)
        {
            var fact = _facts[Key(moon)];
            return Counts(fact, moon) ? CollectionOutcome.Counted : CollectionOutcome.Uncounted;
        }
    }

    public bool SetPending(Moon moon) => ApplyLocal(RunEventKind.SetPending, moon, false);
    public bool SetCounted(Moon moon) => ApplyLocal(RunEventKind.SetCounted, moon, false);
    public bool SetUncounted(Moon moon) => ApplyLocal(RunEventKind.SetUncounted, moon, false);
    public bool Remove(Moon moon) => ApplyLocal(RunEventKind.RemoveMoon, moon, false);

    public void ResetLocal()
    {
        lock (_sync) _facts.Clear();
        Project();
    }

    public void Reproject() => Project();

    public void ApplySharedConfiguration(RunCategory category, bool includePostGame)
    {
        _state.Settings.Category = category;
        _state.Settings.IncludePostGameKingdoms = includePostGame;
        if (!includePostGame && MoonRepository.IsPostGameKingdomName(_state.CurrentKingdom))
            _state.SetKingdom(GameState.InitialKingdom);
        Project();
    }

    public bool ApplyRemote(SharedRunEvent runEvent)
    {
        var moon = Catalog.Resolve(runEvent.Moon);
        return moon != null && Apply(runEvent.Kind, moon);
    }

    private bool ApplyLocal(RunEventKind kind, Moon moon, bool automaticCapture)
    {
        var changed = Apply(kind, moon);
        if (changed)
        {
            LocalEventCreated?.Invoke(this, new SharedRunEvent(
                Guid.NewGuid(),
                kind,
                Catalog.ToWire(moon),
                automaticCapture));
        }
        return changed;
    }

    private bool Apply(RunEventKind kind, Moon moon)
    {
        bool changed;
        lock (_sync)
        {
            var key = Key(moon);
            _facts.TryGetValue(key, out var current);
            var hadCurrent = _facts.ContainsKey(key);
            var next = RunFactReducer.Apply(hadCurrent ? current : null, kind);
            changed = next.HasValue
                ? !hadCurrent || current != next.Value
                : hadCurrent;
            if (!changed) return false;
            if (next.HasValue) _facts[key] = next.Value;
            else _facts.Remove(key);
        }
        Project();
        return true;
    }

    private void Project()
    {
        Dictionary<string, (List<Moon> Pending, List<Moon> Counted, List<Moon> Wrong)> projected =
            new(StringComparer.OrdinalIgnoreCase);
        lock (_sync)
        {
            foreach (var item in _facts)
            {
                var moon = _repository.Moons.FirstOrDefault(candidate =>
                    candidate.Id == item.Key.MoonId &&
                    candidate.Kingdom.Equals(item.Key.Kingdom, StringComparison.OrdinalIgnoreCase));
                if (moon == null) continue;
                var fact = item.Value;
                if (fact.Hinted && !fact.Collected)
                {
                    Get(projected, moon.Kingdom).Pending.Add(moon);
                    if (moon.IsHintArt)
                        Get(projected, moon.CollectionLocationKingdom).Pending.Add(moon);
                }
                if (!fact.Collected) continue;
                if (Counts(fact, moon)) Get(projected, moon.Kingdom).Counted.Add(moon);
                else Get(projected, moon.Kingdom).Wrong.Add(moon);
            }
        }

        var current = _state.CreateSnapshot();
        var states = projected.ToDictionary(
            item => item.Key,
            item => new KingdomStateSnapshot(item.Value.Pending, item.Value.Counted, item.Value.Wrong),
            StringComparer.OrdinalIgnoreCase);
        if (!states.ContainsKey(current.CurrentKingdom))
            states[current.CurrentKingdom] = new KingdomStateSnapshot([], [], []);
        _state.RestoreRun(current.CurrentKingdom, _state.Settings, states);
    }

    private bool Counts(RunFact fact, Moon moon) => fact.ManualClassification switch
    {
        ManualClassification.Counted => true,
        ManualClassification.Uncounted => false,
        _ => moon.IsStory ? _state.Settings.AllowsStoryMoons : fact.Hinted
    };

    private static MoonFactKey Key(Moon moon) => MoonFactKey.FromMoon(moon);

    private static (List<Moon> Pending, List<Moon> Counted, List<Moon> Wrong) Get(
        Dictionary<string, (List<Moon> Pending, List<Moon> Counted, List<Moon> Wrong)> states,
        string kingdom)
    {
        if (!states.TryGetValue(kingdom, out var state))
            states[kingdom] = state = ([], [], []);
        return state;
    }
}
