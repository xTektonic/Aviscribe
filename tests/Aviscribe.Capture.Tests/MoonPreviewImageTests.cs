using System.Security.Cryptography;
using System.Text;
using Aviscribe.Core;
using Aviscribe.Core.Integration;
using SMOVision.Assets;

namespace Aviscribe.Capture.Tests;

public class MoonPreviewImageTests
{
    [Fact]
    public void PackagedPreviewsPreserveEveryLegacyMoonImage()
    {
        var moons = MoonRepository.LoadDefault().Moons;
        Assert.Equal(836, moons.Count);
        var rows = moons.Select(moon =>
        {
            // Use the same owning-kingdom conversion as the pending-list UI,
            // including Bowsers, Dark, Darker and cross-kingdom hint-art rewards.
            var id = SmoCatalog.MoonId(moon);
            using var image = MoonImages.OpenRead(id);
            Assert.NotNull(image);
            return $"{id.Kingdom.Value}/{id.LocalId}:{Convert.ToHexStringLower(SHA256.HashData(image))}";
        }).Order(StringComparer.Ordinal);

        // Recorded from all 836 Aviscribe PNGs before removing the duplicate assets.
        Assert.Equal("d7c12f6d38ffb57b13ebb96629ef44cd256faa272ecdb222858fd356a01418b6",
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', rows)))));
    }
}
