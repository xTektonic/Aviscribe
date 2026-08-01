using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Ocr;

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

        public void Save(
            string path,
            GameStateSnapshot snapshot,
            bool writeOverlay,
            string overlayOutputPath,
            string captureDeviceId = "",
            IReadOnlyDictionary<string, CaptureCropSettings>? captureCropsByDevice = null,
            IEnumerable<KingdomAmbiguousReview>? ambiguousReviews = null)
        {
            var state = new SavedRunState
            {
                CurrentKingdom = snapshot.CurrentKingdom,
                Settings = new RunSettings
                {
                    Category = snapshot.Category,
                    IncludePostGameKingdoms = snapshot.IncludePostGameKingdoms,
                    InputLanguage = snapshot.InputLanguage,
                    OutputLanguage = snapshot.OutputLanguage,
                    WoodedBeforeLake = snapshot.WoodedBeforeLake,
                    SeasideBeforeSnow = snapshot.SeasideBeforeSnow,
                    ShowPendingMoonImages = snapshot.ShowPendingMoonImages,
                    DebugLogging = snapshot.DebugLogging,
                    FocusMoonNumberHotkey = snapshot.FocusMoonNumberHotkey,
                    MoveToPendingHotkey = snapshot.MoveToPendingHotkey,
                    MoveToCountedHotkey = snapshot.MoveToCountedHotkey,
                    MoveToWrongHotkey = snapshot.MoveToWrongHotkey,
                    RemoveMoonHotkey = snapshot.RemoveMoonHotkey
                },
                WriteOverlay = writeOverlay,
                OverlayOutputPath = overlayOutputPath,
                CaptureDeviceId = captureDeviceId,
                CaptureCropsByDevice = (captureCropsByDevice ??
                        new Dictionary<string, CaptureCropSettings>())
                    .ToDictionary(
                        item => item.Key,
                        item => item.Value.Clone(),
                        StringComparer.Ordinal),
                PendingMoonIds = snapshot.Pending.Select(moon => moon.Id).ToList(),
                CollectedMoonIds = snapshot.Collected.Select(moon => moon.Id).ToList(),
                UncountedCollectedMoonIds = snapshot.UncountedCollected.Select(moon => moon.Id).ToList(),
                KingdomStates = snapshot.KingdomStates.ToDictionary(
                    item => item.Key,
                    item => new SavedKingdomState
                    {
                        Pending = item.Value.Pending.Select(CreateMoonReference).ToList(),
                        Collected = item.Value.Collected.Select(CreateMoonReference).ToList(),
                        UncountedCollected = item.Value.UncountedCollected.Select(CreateMoonReference).ToList()
                    },
                    StringComparer.OrdinalIgnoreCase),
                AmbiguousReviews = (ambiguousReviews ?? Array.Empty<KingdomAmbiguousReview>())
                    .Select(review => new SavedReviewState
                    {
                        Kingdom = review.Kingdom,
                        Type = review.Result.Type,
                        Text = review.Result.Text,
                        Candidates = review.Result.Candidates
                            .Select(candidate => new SavedReviewCandidate
                            {
                                Kingdom = candidate.Moon.Kingdom,
                                MoonId = candidate.Moon.Id,
                                Score = candidate.Score
                            })
                            .ToList()
                    })
                    .ToList()
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
            if (savedState.KingdomStates?.Count > 0)
            {
                var restoredStates = savedState.KingdomStates
                    .ToDictionary(
                        item => item.Key,
                        item => new KingdomStateSnapshot(
                            ResolveMoons(item.Value.Pending),
                            ResolveMoons(item.Value.Collected),
                            ResolveMoons(item.Value.UncountedCollected)),
                        StringComparer.OrdinalIgnoreCase);

                gameState.RestoreRun(
                    savedState.CurrentKingdom,
                    savedState.Settings,
                    restoredStates);
                return;
            }

            var byId = _repository
                .GetCollectionCandidates(savedState.CurrentKingdom, savedState.Settings)
                .GroupBy(moon => moon.Id)
                .ToDictionary(group => group.Key, group => group.First());

            gameState.Restore(
                savedState.CurrentKingdom,
                savedState.Settings,
                savedState.PendingMoonIds.SelectMany(id => byId.TryGetValue(id, out var moon) ? [moon] : Array.Empty<Moon>()),
                savedState.CollectedMoonIds.SelectMany(id => byId.TryGetValue(id, out var moon) ? [moon] : Array.Empty<Moon>()),
                savedState.UncountedCollectedMoonIds.SelectMany(id => byId.TryGetValue(id, out var moon) ? [moon] : Array.Empty<Moon>()));
        }

        public IReadOnlyList<KingdomAmbiguousReview> RestoreReviews(SavedRunState savedState)
        {
            return (savedState.AmbiguousReviews ?? new List<SavedReviewState>())
                .Where(review => !string.IsNullOrWhiteSpace(review.Kingdom))
                .Select(review => new KingdomAmbiguousReview(
                    review.Kingdom,
                    new AmbiguousOcrResult(
                        review.Type,
                        review.Text,
                        review.Candidates
                            .Select(candidate =>
                            {
                                var moon = ResolveMoon(candidate);
                                return moon == null
                                    ? ((Moon moon, double score)?)null
                                    : (moon, candidate.Score);
                            })
                            .Where(candidate => candidate.HasValue)
                            .Select(candidate => candidate!.Value))))
                .Where(review => review.Result.Candidates.Count > 0)
                .ToList();
        }

        private static SavedMoonReference CreateMoonReference(Moon moon)
        {
            return new SavedMoonReference
            {
                Kingdom = moon.Kingdom,
                MoonId = moon.Id
            };
        }

        private IReadOnlyList<Moon> ResolveMoons(IEnumerable<SavedMoonReference>? references)
        {
            return (references ?? Array.Empty<SavedMoonReference>())
                .Select(ResolveMoon)
                .Where(moon => moon != null)
                .Cast<Moon>()
                .ToList();
        }

        private Moon? ResolveMoon(SavedMoonReference reference)
        {
            return _repository.Moons.FirstOrDefault(moon =>
                moon.Id == reference.MoonId &&
                moon.Kingdom.Equals(reference.Kingdom, StringComparison.OrdinalIgnoreCase));
        }
    }
}
