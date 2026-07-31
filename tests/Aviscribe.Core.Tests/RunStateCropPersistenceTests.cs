using Aviscribe.Core.Capture;
using System.Text.Json;

namespace Aviscribe.Core.Tests;

public sealed class RunStateCropPersistenceTests
{
    [Fact]
    public void LegacyStateWithoutCropDataLoadsSafely()
    {
        var state = JsonSerializer.Deserialize<SavedRunState>(
            """{"CurrentKingdom":"Cascade","CaptureDeviceId":"old-device"}""");

        Assert.NotNull(state);
        Assert.Empty(state.CaptureCropsByDevice);
    }

    [Fact]
    public void PerDeviceCropsRoundTripWithoutAliasing()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "AviscribeTests",
            Guid.NewGuid().ToString("N"));
        var path = Path.Combine(root, "run-state.json");
        try
        {
            var state = new GameState();
            var store = new RunStateStore(new MoonRepository());
            var crop = new CaptureCropSettings
            {
                SourceWidth = 2560,
                SourceHeight = 1440,
                X = 320,
                Y = 180,
                Width = 1920,
                Height = 1080
            };
            var crops = new Dictionary<string, CaptureCropSettings>
            {
                ["linux:v4l2:abc"] = crop
            };

            store.Save(
                path,
                state.CreateSnapshot(),
                writeOverlay: false,
                overlayOutputPath: "pending.txt",
                captureDeviceId: "linux:v4l2:abc",
                captureCropsByDevice: crops);
            crop.X = 999;

            var loaded = store.Load(path);

            Assert.NotNull(loaded);
            Assert.Equal("linux:v4l2:abc", loaded.CaptureDeviceId);
            Assert.Equal(
                320,
                loaded.CaptureCropsByDevice["linux:v4l2:abc"].X);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
