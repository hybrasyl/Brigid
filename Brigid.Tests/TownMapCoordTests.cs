using Brigid.Controls.World.Popups;
using Xunit;

namespace Brigid.Tests;

// The X in national.dat's _tcoord.txt is the town image's left offset, but the file writes its sign three ways: "-74,71",
// "- 74,71" (a space after the minus) and "120 101" (no sign). The old code negated X, so the last two forms gave a
// negative offset, and those town images (Suomi among them) sat off the left edge of the frame and over the HUD. The
// lines and image widths below are from the retail national.dat.
public class TownMapCoordTests
{
    private const int FRAME_WIDTH = 568;

    [Theory]
    [InlineData("밀레스 500 Mileth -63,78 100 100", 500, 63, 78)]
    [InlineData("수오미 503 Suomi - 74,71 100 100", 503, 74, 71)]
    [InlineData("동뤼케시온 2008 East Rucesion - 183,132 50 50", 2008, 183, 132)]
    [InlineData("북아벨 2013 North Abel 120 101 70 78", 2013, 120, 101)]
    [InlineData("서뤼케시온 2006 West Rucesion 163 125 50 50", 2006, 163, 125)]
    [InlineData("우드랜드대기실 995 (통합) Woodland -102,88 30 30", 995, 102, 88)]
    public void ParseCoordLine_ReadsTheLeftOffsetWhateverItsSign(string line, int mapId, int imageX, int y)
    {
        var entry = TownMapControl.ParseCoordLine(line);

        Assert.NotNull(entry);
        Assert.Equal(mapId, entry.Value.MapId);
        Assert.Equal(imageX, entry.Value.ImageX);
        Assert.Equal(y, entry.Value.Y);
    }

    [Theory]
    [InlineData("수오미 503 Suomi - 74,71 100 100", 432)]
    [InlineData("동뤼케시온 2008 East Rucesion - 183,132 50 50", 216)]
    [InlineData("루어스 664 Loures - 71,83 100 100", 433)]
    [InlineData("타고르 662 Tagor  - 69,69 100 100", 432)]
    [InlineData("호엔 663 Hoen   - 72,76 100 100", 432)]
    [InlineData("북아벨 2013 North Abel 120 101 70 78", 320)]
    [InlineData("서뤼케시온 2006 West Rucesion 163 125 50 50", 216)]
    [InlineData("밀레스 500 Mileth -63,78 100 100", 432)]
    public void ParseCoordLine_PutsEachTownImageInsideTheFrame(string line, int imageWidth)
    {
        var entry = TownMapControl.ParseCoordLine(line)!.Value;

        Assert.InRange(entry.ImageX, 0, FRAME_WIDTH - imageWidth);
    }

    [Theory]
    [InlineData("")]
    [InlineData("우드랜드대기실 (통합) Woodland")]
    [InlineData("503 Suomi - 74,71")]
    public void ParseCoordLine_RefusesALineWithFewerThanFiveNumbers(string line)
        => Assert.Null(TownMapControl.ParseCoordLine(line));
}
