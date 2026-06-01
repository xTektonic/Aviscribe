using System;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Aviscribe.Core
{
    public sealed class RunStateStore
    {
        private readonly MoonRepository _repository;

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true
        };

        public RunStateStore(MoonRepository repository)
        {
            _repository = repository;
        }

        public SavedRunState? Load(string path)
        {
            if (!File.Exists(path))
                return null;

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<SavedRunState>(json, JsonOptions);
        }

        public void Save(string path, GameStateSnapshot snapshot, bool writeOverlay, string overlayOutputPath)
        {
            var state = new SavedRunState
            {
                CurrentKingdom = snapshot.CurrentKingdom,
                Settings = new RunSettings
                {
                    Category = snapshot.Category,
                    IncludePostGameKingdoms = snapshot.IncludePostGameKingdoms,
                    InputLanguage = snapshot.InputLanguage,
                    OutputLanguage = snapshot.OutputLanguage
                },
                WriteOverlay = writeOverlay,
                OverlayOutputPath = overlayOutputPath,
                PendingMoonIds = snapshot.Pending.Select(moon => moon.Id).ToList(),
                CollectedMoonIds = snapshot.Collected.Select(moon => moon.Id).ToList(),
                UncountedCollectedMoonIds = snapshot.UncountedCollected.Select(moon => moon.Id).ToList()
            };

            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(tempPath, path, overwrite: true);
        }

        public void Restore(GameState gameState, SavedRunState savedState)
        {
            var byId = _repository.Moons.ToDictionary(moon => moon.Id);

            gameState.Restore(
                savedState.CurrentKingdom,
                savedState.Settings,
                savedState.PendingMoonIds.SelectMany(id => byId.TryGetValue(id, out var moon) ? [moon] : Array.Empty<Moon>()),
                savedState.CollectedMoonIds.SelectMany(id => byId.TryGetValue(id, out var moon) ? [moon] : Array.Empty<Moon>()),
                savedState.UncountedCollectedMoonIds.SelectMany(id => byId.TryGetValue(id, out var moon) ? [moon] : Array.Empty<Moon>()));
        }
    }
}
