using System.Text.Json;
using Aviscribe.Validation;
using Aviscribe.Core;
using Aviscribe.Core.Capture;
using Aviscribe.Core.Diagnostics;
using Aviscribe.Core.Integration;
using Aviscribe.Core.Ocr;
using Aviscribe.Core.Online;
using System.Security.Cryptography;
using SMOVision.Core.Capture;
using SMOVision.Vision;
using SMOVision.Vision.Ocr.Onnx;

try
{
    switch (args.FirstOrDefault() ?? "help")
    {
        case "state-smoke": StateSmoke.Run(); break;
        case "smovision-smoke":
        {
            var repository = MoonRepository.LoadDefault();
            if (repository.Moons.Count != 836) throw new InvalidDataException("Expected 836 packaged moons.");
            if (!File.Exists(BundledOcrAssets.ModelFile) || !File.Exists(BundledOcrAssets.DictionaryFile))
                throw new FileNotFoundException("Packaged SMOVision OCR assets are missing.");
            foreach (var (file, hash) in new[] { (BundledOcrAssets.ModelFile, BundledOcrAssets.ModelSha256), (BundledOcrAssets.DictionaryFile, BundledOcrAssets.DictionarySha256) })
            {
                using var stream = File.OpenRead(file);
                if (Convert.ToHexStringLower(SHA256.HashData(stream)) != hash)
                    throw new InvalidDataException($"Packaged OCR asset checksum mismatch: {file}");
            }
            using var frame = FrameBuffer.Copy(Enumerable.Repeat((byte)255, 320 * 48 * 3).ToArray(), 320, 48, 960,
                PixelFormat.Bgr24, new("smoke", Guid.Empty, 0, 0, TimeSpan.Zero));
            foreach (var language in Enum.GetValues<GameLanguage>().Where(GameLanguageCatalog.IsSupportedInputLanguage))
            {
                await using var reader = new OnnxTextReader(OnnxReaderOptions.ForLanguage(SmoCatalog.Language(language), forceCpu: true));
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                _ = await reader.ReadAsync(new RegionView(frame, new(0, 0, 320, 48)), timeout.Token);
                if (reader.Runtime.ActiveProvider != "CPU") throw new InvalidOperationException("Expected CPU OCR inference.");
                Console.WriteLine($"CPU OCR inference passed: {language}.");
            }
            _ = new SmoVideoProvider(NullAppDiagnostics.Instance);
            Console.WriteLine($"SMOVision beta.2 smoke passed: {repository.Moons.Count} moons; online catalog {new OnlineCatalog(repository).Hash}.");
            break;
        }
        case "replay" when args.Length >= 4:
        {
            var repository = MoonRepository.LoadDefault();
            var state = new GameState();
            state.Settings.IncludePostGameKingdoms = true;
            state.Settings.OcrMode = args.Contains("--cpu") ? OcrMode.Cpu : OcrMode.WebGpu;
            state.Settings.AdaptiveTalkatooDetection = args.Contains("--adaptive");
            state.SetKingdom(SmoCatalog.KingdomName(SmoCatalog.Kingdom(args[2])));
            var coordinator = new RunCoordinator(state, repository);
            await using var session = new SmoGameSession(state, repository, CaptureCropSettings.Default, NullAppDiagnostics.Instance, coordinator);
            var reviews = new List<object>();
            session.AmbiguousMatchReceived += (_, review) => reviews.Add(new { state.CurrentKingdom, review.Type, review.Text, Candidates = review.Candidates.Select(c => new { c.Moon.Kingdom, c.Moon.Id, c.Score }).ToArray() });
            await session.RunAsync(new FfmpegVideoSource(args[1]));
            var facts = coordinator.CreateFactSnapshot();
            var jsonOptions = new JsonSerializerOptions { WriteIndented = true };
            await File.WriteAllTextAsync(args[3], JsonSerializer.Serialize(new { Facts = facts, Reviews = reviews, Ocr = session.RuntimeStatus, Analysis = session.Result }, jsonOptions));
            if (session.Result?.Status != SMOVision.Vision.AnalysisStatus.Completed || session.Result.Detectors.Values.Any(d => d.Faults > 0))
                throw new InvalidOperationException("Replay failed or a detector faulted; inspect the output.");
            if (args.Length > 4 && !args[4].StartsWith("--", StringComparison.Ordinal))
            {
                var expected = JsonSerializer.Deserialize<List<RunFactSnapshot>>(await File.ReadAllTextAsync(args[4]))!;
                if (!facts.SequenceEqual(expected.OrderBy(f => f.Kingdom, StringComparer.OrdinalIgnoreCase).ThenBy(f => f.MoonId)))
                    throw new InvalidDataException("Replay facts differ from the supplied expected facts.");
            }
            Console.WriteLine($"Replay completed: {session.Result.Frames} frames, {facts.Count} moon facts, {reviews.Count} reviews.");
            break;
        }
        default:
            Console.WriteLine("Commands: state-smoke | smovision-smoke | replay <video> <initial-kingdom> <output.json> [expected-facts.json] [--cpu] [--adaptive]");
            return 2;
    }
    return 0;
}
catch (Exception error) { Console.Error.WriteLine(error); return 1; }
