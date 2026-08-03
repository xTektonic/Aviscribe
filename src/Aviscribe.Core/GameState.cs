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
                Pending.Clear();
                Collected.Clear();
                UncountedCollected.Clear();
            }

            OnChanged();
        }

        public void ResetRun()
        {
            lock (_sync)
            {
                ResetRunState();
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
                    ResetRunState();
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
                if (!ContainsMoon(Pending, moon) &&
                    !ContainsMoon(Collected, moon) &&
                    !ContainsMoon(UncountedCollected, moon))
                {
                    Pending.Add(moon);
                    changed = true;
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
                changed = RemoveMoon(Pending, moon) || changed;
                changed = RemoveMoon(Collected, moon) || changed;
                changed = RemoveMoon(UncountedCollected, moon) || changed;
                Pending.Add(moon);
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

            lock (_sync)
            {
                if (ContainsMoon(Collected, moon))
                    return CollectionOutcome.AlreadyCounted;

                if (ContainsMoon(UncountedCollected, moon))
                    return CollectionOutcome.AlreadyUncounted;

                var countsForRules = moon.IsStory
                    ? Settings.AllowsStoryMoons
                    : ContainsMoon(Pending, moon);

                RemoveMoon(Pending, moon);
                RemoveMoon(Collected, moon);
                RemoveMoon(UncountedCollected, moon);
                if (countsForRules)
                {
                    Collected.Add(moon);
                    outcome = CollectionOutcome.Counted;
                }
                else
                {
                    UncountedCollected.Add(moon);
                    outcome = CollectionOutcome.Uncounted;
                }
            }

            OnChanged();
            return outcome;
        }

        public CollectionOutcome MarkUncounted(Moon moon)
        {
            if (moon == null) return CollectionOutcome.Ignored;

            lock (_sync)
            {
                if (ContainsMoon(UncountedCollected, moon))
                    return CollectionOutcome.AlreadyUncounted;

                RemoveMoon(Pending, moon);
                RemoveMoon(Collected, moon);
                RemoveMoon(UncountedCollected, moon);
                UncountedCollected.Add(moon);
            }

            OnChanged();
            return CollectionOutcome.Uncounted;
        }

        public bool MoveToCollected(Moon moon)
        {
            if (moon == null) return false;

            var changed = false;

            lock (_sync)
            {
                changed = RemoveMoon(Pending, moon) || changed;
                changed = RemoveMoon(Collected, moon) || changed;
                changed = RemoveMoon(UncountedCollected, moon) || changed;
                Collected.Add(moon);
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
                changed = RemoveMoon(Pending, moon) || changed;
                changed = RemoveMoon(Collected, moon) || changed;
                changed = RemoveMoon(UncountedCollected, moon) || changed;
                UncountedCollected.Add(moon);
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
                changed = RemoveMoon(Pending, moon);
                changed = RemoveMoon(Collected, moon) || changed;
                changed = RemoveMoon(UncountedCollected, moon) || changed;
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
                _kingdomStates.Clear();
                CurrentKingdom = kingdom;
                Settings.CopyFrom(settings);
                var restored = CreateKingdomState(pending, collected, uncountedCollected);
                _kingdomStates[kingdom] = restored;
                ApplyKingdomState(restored);
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
                _kingdomStates.Clear();
                Settings.CopyFrom(settings);

                foreach (var item in kingdomStates.Where(item =>
                             !string.IsNullOrWhiteSpace(item.Key)))
                {
                    _kingdomStates[item.Key] = CreateKingdomState(
                        item.Value.Pending,
                        item.Value.Collected,
                        item.Value.UncountedCollected);
                }

                CurrentKingdom = currentKingdom;
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
                    Settings.ShowPendingMoonImages,
                    Settings.DebugLogging,
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

        private void ResetRunState()
        {
            _kingdomStates.Clear();
            CurrentKingdom = InitialKingdom;
            ApplyKingdomState(GetOrCreateKingdomState(InitialKingdom));
        }

        private static KingdomStateData CreateKingdomState(
            IEnumerable<Moon> pending,
            IEnumerable<Moon> collected,
            IEnumerable<Moon> uncountedCollected)
        {
            var collectedList = DistinctMoons(collected);
            var uncountedList = DistinctMoons(uncountedCollected)
                .Where(moon => !ContainsMoon(collectedList, moon))
                .ToList();
            var pendingList = DistinctMoons(pending)
                .Where(moon =>
                    !ContainsMoon(collectedList, moon) &&
                    !ContainsMoon(uncountedList, moon))
                .ToList();

            return new KingdomStateData(pendingList, collectedList, uncountedList);
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
        bool ShowPendingMoonImages,
        bool DebugLogging,
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
