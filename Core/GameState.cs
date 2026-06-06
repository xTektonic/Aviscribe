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
        private readonly object _sync = new();

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
            var changed = false;

            lock (_sync)
            {
                if (CurrentKingdom != kingdom)
                {
                    CurrentKingdom = kingdom;
                    Pending.Clear();
                    Collected.Clear();
                    UncountedCollected.Clear();
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
                if (!Pending.Contains(moon) && !Collected.Contains(moon) && !UncountedCollected.Contains(moon))
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
                changed = Collected.Remove(moon) || changed;
                changed = UncountedCollected.Remove(moon) || changed;

                if (!Pending.Contains(moon))
                {
                    Pending.Add(moon);
                    changed = true;
                }
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
                if (Collected.Contains(moon))
                    return CollectionOutcome.AlreadyCounted;

                if (UncountedCollected.Contains(moon))
                    return CollectionOutcome.AlreadyUncounted;

                var countsForRules = moon.IsStory
                    ? Settings.AllowsStoryMoons
                    : Pending.Remove(moon);

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
                if (Collected.Contains(moon))
                    return CollectionOutcome.AlreadyCounted;

                if (UncountedCollected.Contains(moon))
                    return CollectionOutcome.AlreadyUncounted;

                Pending.Remove(moon);
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
                changed = Pending.Remove(moon) || changed;
                changed = UncountedCollected.Remove(moon) || changed;

                if (!Collected.Contains(moon))
                {
                    Collected.Add(moon);
                    changed = true;
                }
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
                changed = Pending.Remove(moon) || changed;
                changed = Collected.Remove(moon) || changed;

                if (!UncountedCollected.Contains(moon))
                {
                    UncountedCollected.Add(moon);
                    changed = true;
                }
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
                changed = Pending.Remove(moon);
                changed = Collected.Remove(moon) || changed;
                changed = UncountedCollected.Remove(moon) || changed;
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
                Pending = pending.Distinct().ToList();
                Collected = collected.Distinct().ToList();
                UncountedCollected = uncountedCollected.Distinct().ToList();
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
                    Pending.ToList(),
                    Collected.ToList(),
                    UncountedCollected.ToList(),
                    Collected.Sum(m => m.MoonCountValue),
                    Collected.Concat(UncountedCollected).Sum(m => m.MoonCountValue));
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
    }

    public sealed record GameStateSnapshot(
        string CurrentKingdom,
        RunCategory Category,
        bool IncludePostGameKingdoms,
        GameLanguage InputLanguage,
        GameLanguage OutputLanguage,
        IReadOnlyList<Moon> Pending,
        IReadOnlyList<Moon> Collected,
        IReadOnlyList<Moon> UncountedCollected,
        int CountedMoonCount,
        int ActualMoonCount);
}
