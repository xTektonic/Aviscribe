using Aviscribe.Core;

namespace Aviscribe.Classifier
{
    internal static class StateSmoke
    {
        public static void Run()
        {
            var state = new GameState();
            state.SetKingdom("Cascade");
            state.Settings.Category = RunCategory.Hardcore;

            var storyMulti = new Moon
            {
                Id = 1,
                Kingdom = "Cascade",
                English = "Multi Moon Atop the Falls",
                IsStory = true,
                IsMulti = true
            };

            Expect(
                state.MarkCollected(storyMulti) == CollectionOutcome.Uncounted,
                "Hardcore story multi moon should be tracked as uncounted.");
            Expect(state.ActualMoonCount == 3, "Actual moon count should include uncounted multi moons.");
            Expect(state.CountedMoonCount == 0, "Hardcore story multi moon should not count for rules.");

            var talkatooMoon = new Moon
            {
                Id = 2,
                Kingdom = "Cascade",
                English = "Behind the Waterfall"
            };

            state.AddPending(talkatooMoon);
            Expect(state.Pending.Count == 1, "Talkatoo moon should be pending.");
            Expect(
                state.MarkCollected(talkatooMoon) == CollectionOutcome.Counted,
                "Pending non-story moon should count when collected.");
            Expect(state.ActualMoonCount == 4, "Actual moon count should include counted and uncounted moons.");
            Expect(state.CountedMoonCount == 1, "Counted moon count should include pending non-story collection.");

            var wrongMoon = new Moon
            {
                Id = 3,
                Kingdom = "Cascade",
                English = "Wrong Moon"
            };

            Expect(
                state.MarkCollected(wrongMoon) == CollectionOutcome.Uncounted,
                "Unmentioned non-story moon should be tracked as uncounted.");
            Expect(state.ActualMoonCount == 5, "Actual moon count should include wrong non-story moon.");
            Expect(state.CountedMoonCount == 1, "Wrong non-story moon should not count for rules.");

            state.AddPending(wrongMoon);
            Expect(state.Pending.Count == 0, "Already collected wrong moon should not become pending later.");

            var manualWrongMoon = new Moon
            {
                Id = 4,
                Kingdom = "Cascade",
                English = "Manual Wrong Moon"
            };

            state.AddPending(manualWrongMoon);
            Expect(
                state.MarkUncounted(manualWrongMoon) == CollectionOutcome.Uncounted,
                "Manual wrong mark should move a pending moon into uncounted.");
            Expect(!state.Pending.Contains(manualWrongMoon), "Manual wrong mark should remove a pending moon.");
            Expect(state.UncountedCollected.Contains(manualWrongMoon), "Manual wrong mark should track the moon as uncounted.");

            Expect(state.Remove(manualWrongMoon), "Manual remove should remove a tracked moon.");
            Expect(!state.UncountedCollected.Contains(manualWrongMoon), "Manual remove should clear uncounted state.");

            state.ResetKingdom();
            Expect(state.Pending.Count == 0, "Reset should clear pending moons.");
            Expect(state.Collected.Count == 0, "Reset should clear counted moons.");
            Expect(state.UncountedCollected.Count == 0, "Reset should clear uncounted moons.");
            Expect(state.ActualMoonCount == 0, "Reset should clear actual count.");
            Expect(state.CountedMoonCount == 0, "Reset should clear counted count.");

            state.AddPending(talkatooMoon);
            var pendingText = RunStateTextFormatter.FormatPending(state.CreateSnapshot());
            Expect(pendingText == talkatooMoon.English, "Pending formatter should write pending moon names.");

            var outputPath = Path.Combine(Path.GetTempPath(), $"aviscribe-state-smoke-{Guid.NewGuid():N}.txt");
            try
            {
                var writer = new RunOutputWriter { OutputPath = outputPath };
                writer.WritePending(state.CreateSnapshot());
                Expect(File.ReadAllText(outputPath) == talkatooMoon.English, "Output writer should write pending text.");
            }
            finally
            {
                if (File.Exists(outputPath))
                    File.Delete(outputPath);
            }

            var repo = new MoonRepository();
            repo.Moons.Add(talkatooMoon);
            var store = new RunStateStore(repo);
            var statePath = Path.Combine(Path.GetTempPath(), $"aviscribe-state-smoke-{Guid.NewGuid():N}.json");
            try
            {
                store.Save(statePath, state.CreateSnapshot(), writeOverlay: true, overlayOutputPath: outputPath);
                var saved = store.Load(statePath);
                Expect(saved != null, "Saved state should load.");

                var restored = new GameState();
                store.Restore(restored, saved!);
                Expect(restored.Pending.Count == 1, "Saved state should restore pending moons.");
                Expect(restored.Pending[0].Id == talkatooMoon.Id, "Saved state should restore the pending moon id.");
            }
            finally
            {
                if (File.Exists(statePath))
                    File.Delete(statePath);
            }

            Console.WriteLine("State smoke passed.");
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
