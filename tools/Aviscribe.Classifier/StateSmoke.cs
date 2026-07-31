using Aviscribe.Core;
using Aviscribe.Core.Capture;

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

            var manualMoon = new Moon
            {
                Id = 5,
                Kingdom = "Cascade",
                English = "Manual UI Moon"
            };

            Expect(state.MoveToPending(manualMoon), "Manual moon list click should move a moon to pending.");
            Expect(state.Pending.Contains(manualMoon), "Manual moon should be pending after moon list click.");
            Expect(state.MoveToCollected(manualMoon), "Dragging pending to collected should move the moon.");
            Expect(!state.Pending.Contains(manualMoon), "Dragging to collected should remove pending state.");
            Expect(state.Collected.Contains(manualMoon), "Dragging to collected should track counted state.");
            Expect(state.MoveToPending(manualMoon), "Clicking collected should move the moon back to pending.");
            Expect(!state.Collected.Contains(manualMoon), "Move to pending should clear collected state.");
            Expect(state.Pending.Contains(manualMoon), "Move to pending should restore pending state.");
            Expect(state.MoveToUncounted(manualMoon), "Dragging pending to wrong moons should move the moon.");
            Expect(!state.Pending.Contains(manualMoon), "Dragging to wrong moons should clear pending state.");
            Expect(state.UncountedCollected.Contains(manualMoon), "Dragging to wrong moons should track uncounted state.");
            Expect(state.MoveToPending(manualMoon), "Clicking wrong moon should move it back to pending.");
            Expect(!state.UncountedCollected.Contains(manualMoon), "Move to pending should clear uncounted state.");
            Expect(state.Pending.Contains(manualMoon), "Move to pending from wrong moons should restore pending state.");

            state.MoveToCollected(manualMoon);
            state.MoveToUncounted(manualMoon);
            Expect(!state.Pending.Contains(manualMoon), "Exclusive state should clear pending when moved to wrong.");
            Expect(!state.Collected.Contains(manualMoon), "Exclusive state should clear counted when moved to wrong.");
            Expect(state.UncountedCollected.Contains(manualMoon), "Exclusive state should leave the moon in exactly one list.");

            var directCollectedMoon = new Moon
            {
                Id = 6,
                Kingdom = "Cascade",
                English = "Directly Collected Moon"
            };

            Expect(
                state.MarkCollected(directCollectedMoon) == CollectionOutcome.Uncounted,
                "OCR collecting an unmentioned moon should track it as wrong.");
            Expect(state.UncountedCollected.Contains(directCollectedMoon), "Direct unmentioned collection should be uncounted.");
            Expect(state.MoveToCollected(directCollectedMoon), "Dragging a wrong moon to counted should manually correct it.");
            Expect(!state.UncountedCollected.Contains(directCollectedMoon), "Manual counted correction should clear wrong state.");
            Expect(state.Collected.Contains(directCollectedMoon), "Manual counted correction should track counted state.");

            var directManualCollectedMoon = new Moon
            {
                Id = 7,
                Kingdom = "Cascade",
                English = "Direct Manual Collected Moon"
            };

            Expect(
                state.MoveToCollected(directManualCollectedMoon),
                "Dragging from the full moon list directly to collected should move the moon to counted.");
            Expect(
                state.Collected.Contains(directManualCollectedMoon),
                "Direct full-list to collected drag should track counted state.");
            Expect(
                !state.UncountedCollected.Contains(directManualCollectedMoon),
                "Direct full-list to collected drag should not route through wrong moon state.");

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
                var captureCrops = new Dictionary<string, CaptureCropSettings>
                {
                    ["obs-camera"] = CaptureCropSettings.FromRect(
                        1920,
                        1080,
                        new OpenCvSharp.Rect(160, 90, 1600, 900)),
                    ["capture-card"] = CaptureCropSettings.Default
                };
                store.Save(
                    statePath,
                    state.CreateSnapshot(),
                    writeOverlay: true,
                    overlayOutputPath: outputPath,
                    captureDeviceId: "obs-camera",
                    captureCropsByDevice: captureCrops);
                var saved = store.Load(statePath);
                Expect(saved != null, "Saved state should load.");
                Expect(saved!.CaptureDeviceId == "obs-camera", "Saved state should restore the selected capture device.");
                Expect(saved.CaptureCropsByDevice.Count == 2, "Saved state should preserve per-device crops.");
                Expect(
                    saved.CaptureCropsByDevice["obs-camera"].Width == 1600,
                    "Saved state should preserve crop dimensions.");

                var restored = new GameState();
                store.Restore(restored, saved);
                Expect(restored.Pending.Count == 1, "Saved state should restore pending moons.");
                Expect(restored.Pending[0].Id == talkatooMoon.Id, "Saved state should restore the pending moon id.");

                File.WriteAllText(statePath, """{"CaptureDeviceId":"legacy-camera"}""");
                var legacy = store.Load(statePath);
                Expect(legacy != null, "Legacy saved state should load.");
                Expect(
                    legacy!.CaptureCropsByDevice != null &&
                    legacy.CaptureCropsByDevice.Count == 0,
                    "Legacy saved state should default to no device calibrations.");
            }
            finally
            {
                if (File.Exists(statePath))
                    File.Delete(statePath);
            }

            var orderedRepo = new MoonRepository();
            orderedRepo.Moons.Add(new Moon { Id = 1, Kingdom = "Sand", English = "Sand Moon" });
            orderedRepo.Moons.Add(new Moon { Id = 1, Kingdom = "Cascade", English = "Cascade Moon" });
            orderedRepo.Moons.Add(new Moon { Id = 1, Kingdom = "Mushroom", English = "Mushroom Moon" });

            Expect(
                orderedRepo.GetKingdoms(includePostGameKingdoms: false).SequenceEqual(new[] { "Sand", "Cascade" }),
                "Kingdom list should preserve source order and hide postgame kingdoms when disabled.");
            Expect(
                orderedRepo.GetKingdoms(includePostGameKingdoms: true).SequenceEqual(new[] { "Sand", "Cascade", "Mushroom" }),
                "Kingdom list should preserve source order when postgame kingdoms are shown.");

            Console.WriteLine("State smoke passed.");
        }

        private static void Expect(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
