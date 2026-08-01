using Aviscribe.Core.Capture;

namespace Aviscribe.Capture.Tests;

public sealed class CaptureSourceSelectionTests
{
    [Fact]
    public void SwitchingSourceTypeRestoresLastSourceOfEachType()
    {
        var camera = Source("camera:1", CaptureSourceKind.VideoDevice);
        var otherCamera = Source("camera:2", CaptureSourceKind.VideoDevice);
        var window = Source("window:1", CaptureSourceKind.Window);
        var selection = new CaptureSourceSelection();

        selection.Select(otherCamera);
        selection.Select(window);
        selection.SetKind(CaptureSourceKind.VideoDevice);

        Assert.Equal(otherCamera, selection.Restore([camera, otherCamera, window]));
        Assert.Equal([camera, otherCamera], selection.Filter([camera, window, otherCamera]));

        selection.SetKind(CaptureSourceKind.Window);
        Assert.Equal(window, selection.Restore([camera, otherCamera, window]));
    }

    [Fact]
    public void MissingRememberedSourceFallsBackWithinSelectedType()
    {
        var selection = new CaptureSourceSelection(
            CaptureSourceKind.Window,
            new Dictionary<CaptureSourceKind, string>
            {
                [CaptureSourceKind.Window] = "window:gone"
            });
        var camera = Source("camera:1", CaptureSourceKind.VideoDevice);
        var window = Source("window:available", CaptureSourceKind.Window);

        Assert.Equal(window, selection.Restore([camera, window]));
    }

    private static VideoDevice Source(string id, CaptureSourceKind kind) =>
        new() { Id = id, Name = id, Kind = kind };
}
