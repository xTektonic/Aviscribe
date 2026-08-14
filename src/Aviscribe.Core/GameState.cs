using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Aviscribe.Core
{
    public enum CollectionOutcome
    {
        Ignored,
        Counted,
        Uncounted,
        AlreadyCounted,
        AlreadyUncounted
    }

    public class GameState
    {
        public const string InitialKingdom = "Cascade";

        private readonly object _sync = new();
        private readonly Dictionary<string, KingdomStateData> _kingdomStates =
            new(StringComparer.OrdinalIgnoreCase);

        public string CurrentKingdom { get; private set; } = string.Empty;
        public RunSettings Settings { get; } = new();

        public List<Moon> Pending { get; private set; } = new();
        public List<Moon> Collected { get; private set; } = new();
        public List<Moon> UncountedCollected { get; private set; } = new();

        public event EventHandler? Changed;

        public int CountedMoonCount
        {
            get
            {
                lock (_sync)
                    return Collected.Sum(m => m.MoonCountValue);
            }
        }

        public int ActualMoonCount
        {
            get
            {
                lock (_sync)
                    return Collected.Concat(UncountedCollected).Sum(m => m.MoonCountValue);
            }
        }

        public void SetKingdom(string kingdom)
        {
            if (string.IsNullOrWhiteSpace(kingdom))
                return;

            var changed = false;

            lock (_sync)
            {
                if (!CurrentKingdom.Equals(kingdom, StringComparison.OrdinalIgnoreCase))
                {
                    CurrentKingdom = kingdom;
                    ApplyKingdomState(GetOrCreateKingdomState(kingdom));
                    changed = true;
                }
            }

            if (changed)
                OnChanged();
        }

        public void ResetKingdom()
        {
            lock (_sync)
            {
                var mirroredPending = Pending
                    .Where(moon => moon.IsHintArt)
                    .ToList();

                Pending.Clear();
                Collected.Clear();
                UncountedCollected.Clear();

                foreach (var moon in mirroredPending)
                {
                    foreach (var state in _kingdomStates.Values)
                        RemoveMoon(state.Pending, moon);
                }
            }

            OnChanged();
        }

        public void ResetRun()
        {
            lock (_sync)
            {
                ResetRunState(CurrentKingdom);
            }

            OnChanged();
        }

        public bool SetIncludePostGameKingdoms(bool includePostGameKingdoms)
        {
            lock (_sync)
            {
                if (Settings.IncludePostGameKingdoms == includePostGameKingdoms)
                    return false;

                Settings.IncludePostGameKingdoms = includePostGameKingdoms;
                if (!includePostGameKingdoms)
                {
                    var resetKingdom = KingdomRoute.GetRequirement(CurrentKingdom) > 0 &&
                        !MoonRepository.IsPostGameKingdomName(CurrentKingdom)
                        ? CurrentKingdom
                        : InitialKingdom;
                    ResetRunState(resetKingdom);
                }
            }

            OnChanged();
            return true;
        }

        public void AddPending(Moon moon)
        {
            TryAddPending(moon);
        }

        public bool TryAddPending(Moon moon)
        {
            if (moon == null) return false;

            var changed = false;

            lock (_sync)
            {
                var resultState = GetOrCreateKingdomState(moon.Kingdom);
                if (!ContainsMoon(resultState.Collected, moon) &&
                    !ContainsMoon(resultState.UncountedCollected, moon))
                {
                    foreach (var kingdom in GetPendingKingdoms(moon))
                    {
                        var pending = GetOrCreateKingdomState(kingdom).Pending;
                        if (!ContainsMoon(pending, moon))
                        {
                            pending.Add(moon);
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
                OnChanged();

            return changed;
        }

        public bool MoveToPending(Moon moon)
        {
            if (moon == null) return false;

            var changed = false;

            lock (_sync)
            {
                changed = RemoveMoonEverywhere(moon);
                foreach (var kingdom in GetPendingKingdoms(moon))
                    GetOrCreateKingdomState(kingdom).Pending.Add(moon);
                changed = true;
            }

            if (changed)
                OnChanged();

            return changed;
        }

        public CollectionOutcome MarkCollected(Moon moon)
        {
            if (moon == null) return CollectionOutcome.Ignored;

            CollectionOutcome outcome;
            var changed = false;

            lock (_sync)
            {
                var resultState = GetOrCreateKingdomState(moon.Kingdom);
                if (ContainsMoon(resultState.Collected, moon))
                {
                    changed = RemoveMoonFromAllExcept(
                        moon,
                        resultState.Collected);
                    outcome = CollectionOutcome.AlreadyCounted;
                }
                else if (ContainsMoon(resultState.UncountedCollected, moon))
                {
                    changed = RemoveMoonFromAllExcept(
                        moon,
                        resultState.UncountedCollected);
                    outcome = CollectionOutcome.AlreadyUncounted;
                }
                else
                {
                    var countsForRules = moon.IsStory
                        ? Settings.AllowsStoryMoons
                        : IsPendingAnywhere(moon);

                    changed = RemoveMoonEverywhere(moon);
                    if (countsForRules)
                    {
                        resultState.Collected.Add(moon);
                        outcome = CollectionOutcome.Counted;
                    }
                    else
                    {
                        resultState.UncountedCollected.Add(moon);
                        outcome = CollectionOutcome.Uncounted;
                    }
                    changed = true;
                }
            }

            if (changed)
                OnChanged();
            return outcome;
        }

        public CollectionOutcome MarkUncounted(Moon moon)
        {
            if (moon == null) return CollectionOutcome.Ignored;

            CollectionOutcome outcome;
            var changed = false;

            lock (_sync)
            {
                var resultState = GetOrCreateKingdomState(moon.Kingdom);
                if (ContainsMoon(resultState.UncountedCollected, moon))
                {
                    changed = RemoveMoonFromAllExcept(
                        moon,
                        resultState.UncountedCollected);
                    outcome = CollectionOutcome.AlreadyUncounted;
                }
                else
                {
                    changed = RemoveMoonEverywhere(moon);
                    resultState.UncountedCollected.Add(moon);
                    changed = true;
                    outcome = CollectionOutcome.Uncounted;
                }
            }

            if (changed)
                OnChanged();
            return outcome;
        }

        public bool MoveToCollected(Moon moon)
        {
            if (moon == null) return false;

            var changed = false;

            lock (_sync)
            {
                changed = RemoveMoonEverywhere(moon);
                GetOrCreateKingdomState(moon.Kingdom).Collected.Add(moon);
                changed = true;
            }

            if (changed)
                OnChanged();

            return changed;
        }

        public bool MoveToUncounted(Moon moon)
        {
            if (moon == null) return false;

            var changed = false;

            lock (_sync)
            {
                changed = RemoveMoonEverywhere(moon);
                GetOrCreateKingdomState(moon.Kingdom).UncountedCollected.Add(moon);
                changed = true;
            }

            if (changed)
                OnChanged();

            return changed;
        }

        public bool Remove(Moon moon)
        {
            if (moon == null) return false;

            bool changed;

            lock (_sync)
            {
                changed = RemoveMoonEverywhere(moon);
            }

            if (changed)
                OnChanged();

            return changed;
        }

        public void Restore(
            string kingdom,
            RunSettings settings,
            IEnumerable<Moon> pending,
            IEnumerable<Moon> collected,
            IEnumerable<Moon> uncountedCollected)
        {
            lock (_sync)
            {
                CurrentKingdom = kingdom;
                Settings.CopyFrom(settings);
                RestoreKingdomStates(new Dictionary<string, KingdomStateSnapshot>(
                    StringComparer.OrdinalIgnoreCase)
                {
                    [kingdom] = new KingdomStateSnapshot(
                        pending.ToList(),
                        collected.ToList(),
                        uncountedCollected.ToList())
                });
                ApplyKingdomState(GetOrCreateKingdomState(kingdom));
            }

            OnChanged();
        }

        public void RestoreRun(
            string currentKingdom,
            RunSettings settings,
            IReadOnlyDictionary<string, KingdomStateSnapshot> kingdomStates)
        {
            lock (_sync)
            {
                Settings.CopyFrom(settings);
                CurrentKingdom = currentKingdom;
                RestoreKingdomStates(kingdomStates);
                ApplyKingdomState(GetOrCreateKingdomState(currentKingdom));
            }

            OnChanged();
        }

        public GameStateSnapshot CreateSnapshot()
        {
            lock (_sync)
            {
                return new GameStateSnapshot(
                    CurrentKingdom,
                    Settings.Category,
                    Settings.IncludePostGameKingdoms,
                    Settings.InputLanguage,
                    Settings.OutputLanguage,
                    Settings.WoodedBeforeLake,
                    Settings.SeasideBeforeSnow,
                    Settings.AutomaticallySwitchKingdoms,
                    Settings.AdaptiveTalkatooDetection,
                    Settings.ShowPendingMoonImages,
                    Settings.OcrMode,
                    Settings.FocusMoonNumberHotkey,
                    Settings.MoveToPendingHotkey,
                    Settings.MoveToCountedHotkey,
                    Settings.MoveToWrongHotkey,
                    Settings.RemoveMoonHotkey,
                    Pending.ToList(),
                    Collected.ToList(),
                    UncountedCollected.ToList(),
                    Collected.Sum(m => m.MoonCountValue),
                    Collected.Concat(UncountedCollected).Sum(m => m.MoonCountValue),
                    _kingdomStates.ToDictionary(
                        item => item.Key,
                        item => new KingdomStateSnapshot(
                            item.Value.Pending.ToList(),
                            item.Value.Collected.ToList(),
                            item.Value.UncountedCollected.ToList()),
                        StringComparer.OrdinalIgnoreCase));
            }
        }

        public void NotifySettingsChanged()
        {
            OnChanged();
        }

        private void OnChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private static bool ContainsMoon(IEnumerable<Moon> moons, Moon moon)
        {
            return moons.Any(candidate => SameMoon(candidate, moon));
        }

        private static bool RemoveMoon(List<Moon> moons, Moon moon)
        {
            return moons.RemoveAll(candidate => SameMoon(candidate, moon)) > 0;
        }

        private static IReadOnlyList<string> GetPendingKingdoms(Moon moon)
        {
            return moon.IsHintArt
                ? new[] { moon.Kingdom, moon.CollectionLocationKingdom }
                : new[] { moon.Kingdom };
        }

        private bool IsPendingAnywhere(Moon moon)
        {
            return _kingdomStates.Values.Any(state =>
                ContainsMoon(state.Pending, moon));
        }

        private bool RemoveMoonEverywhere(Moon moon)
        {
            var changed = false;
            foreach (var state in _kingdomStates.Values)
            {
                changed = RemoveMoon(state.Pending, moon) || changed;
                changed = RemoveMoon(state.Collected, moon) || changed;
                changed = RemoveMoon(state.UncountedCollected, moon) || changed;
            }

            return changed;
        }

        private bool RemoveMoonFromAllExcept(Moon moon, List<Moon> preservedList)
        {
            var changed = false;
            foreach (var state in _kingdomStates.Values)
            {
                if (!ReferenceEquals(state.Pending, preservedList))
                    changed = RemoveMoon(state.Pending, moon) || changed;
                if (!ReferenceEquals(state.Collected, preservedList))
                    changed = RemoveMoon(state.Collected, moon) || changed;
                if (!ReferenceEquals(state.UncountedCollected, preservedList))
                    changed = RemoveMoon(state.UncountedCollected, moon) || changed;
            }

            return changed;
        }

        private static List<Moon> DistinctMoons(IEnumerable<Moon> moons)
        {
            return moons
                .GroupBy(moon => $"{moon.Kingdom}\0{moon.Id}", StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
        }

        private static bool SameMoon(Moon left, Moon right)
        {
            return left.Id == right.Id &&
                left.Kingdom.Equals(right.Kingdom, StringComparison.OrdinalIgnoreCase);
        }

        private KingdomStateData GetOrCreateKingdomState(string kingdom)
        {
            if (!_kingdomStates.TryGetValue(kingdom, out var state))
            {
                state = new KingdomStateData();
                _kingdomStates[kingdom] = state;
            }

            return state;
        }

        private void ApplyKingdomState(KingdomStateData state)
        {
            Pending = state.Pending;
            Collected = state.Collected;
            UncountedCollected = state.UncountedCollected;
        }

        private void ResetRunState(string kingdom)
        {
            _kingdomStates.Clear();
            CurrentKingdom = string.IsNullOrWhiteSpace(kingdom)
                ? InitialKingdom
                : kingdom;
            ApplyKingdomState(GetOrCreateKingdomState(CurrentKingdom));
        }

        private void RestoreKingdomStates(
            IReadOnlyDictionary<string, KingdomStateSnapshot> kingdomStates)
        {
            _kingdomStates.Clear();

            foreach (var kingdom in kingdomStates.Keys.Where(kingdom =>
                         !string.IsNullOrWhiteSpace(kingdom)))
            {
                GetOrCreateKingdomState(kingdom);
            }

            var collected = DistinctMoons(kingdomStates.Values.SelectMany(state =>
                state.Collected));
            var uncounted = DistinctMoons(kingdomStates.Values.SelectMany(state =>
                    state.UncountedCollected))
                .Where(moon => !ContainsMoon(collected, moon))
                .ToList();
            var pending = DistinctMoons(kingdomStates.Values.SelectMany(state =>
                    state.Pending))
                .Where(moon =>
                    !ContainsMoon(collected, moon) &&
                    !ContainsMoon(uncounted, moon))
                .ToList();

            foreach (var moon in collected)
                GetOrCreateKingdomState(moon.Kingdom).Collected.Add(moon);
            foreach (var moon in uncounted)
                GetOrCreateKingdomState(moon.Kingdom).UncountedCollected.Add(moon);
            foreach (var moon in pending)
            {
                foreach (var kingdom in GetPendingKingdoms(moon))
                    GetOrCreateKingdomState(kingdom).Pending.Add(moon);
            }
        }

        private sealed class KingdomStateData
        {
            public KingdomStateData()
                : this(new List<Moon>(), new List<Moon>(), new List<Moon>())
            {
            }

            public KingdomStateData(
                List<Moon> pending,
                List<Moon> collected,
                List<Moon> uncountedCollected)
            {
                Pending = pending;
                Collected = collected;
                UncountedCollected = uncountedCollected;
            }

            public List<Moon> Pending { get; }
            public List<Moon> Collected { get; }
            public List<Moon> UncountedCollected { get; }
        }
    }

    public sealed record KingdomStateSnapshot(
        IReadOnlyList<Moon> Pending,
        IReadOnlyList<Moon> Collected,
        IReadOnlyList<Moon> UncountedCollected);

    public sealed record GameStateSnapshot(
        string CurrentKingdom,
        RunCategory Category,
        bool IncludePostGameKingdoms,
        GameLanguage InputLanguage,
        GameLanguage OutputLanguage,
        bool WoodedBeforeLake,
        bool SeasideBeforeSnow,
        bool AutomaticallySwitchKingdoms,
        bool AdaptiveTalkatooDetection,
        bool ShowPendingMoonImages,
        Ocr.OcrMode OcrMode,
        string FocusMoonNumberHotkey,
        string MoveToPendingHotkey,
        string MoveToCountedHotkey,
        string MoveToWrongHotkey,
        string RemoveMoonHotkey,
        IReadOnlyList<Moon> Pending,
        IReadOnlyList<Moon> Collected,
        IReadOnlyList<Moon> UncountedCollected,
        int CountedMoonCount,
        int ActualMoonCount,
        IReadOnlyDictionary<string, KingdomStateSnapshot> KingdomStates);
}
