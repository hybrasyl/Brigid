#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.ViewModel;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using SkiaSharp;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-148: the player plate. It shows the name, the level and the class, and the head of the aisling, which is a
///     square at the top of the figure, centred on it, cut from the composite; a left click raises its click.
/// </summary>
[Collection("FontEngine")]
public class HudPlayerPlateTests : IDisposable
{
    public HudPlayerPlateTests() => FontEngine.Initialize(0);

    private readonly List<UIElement> Owned = [];

    public void Dispose()
    {
        foreach (var element in Owned)
            element.Dispose();
    }

    private MainHudControl NewHud(PlayerAttributes attrs)
    {
        var classic = DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>();
        var hud = new MainHudControl(classic, attributes: attrs, chat: new Chat());
        Owned.Add(hud);

        return hud;
    }

    //the alpha of an image of width by height with the pixels of each rect not clear
    private static byte[] Figure(int width, int height, params Rectangle[] ink)
    {
        var alpha = new byte[width * height];

        foreach (var rect in ink)
            for (var y = rect.Top; y < rect.Bottom; y++)
                for (var x = rect.Left; x < rect.Right; x++)
                    alpha[y * width + x] = 255;

        return alpha;
    }

    [Fact]
    public void TheHeadIsASquareAtTheTopOfTheFigureCentredOnIt()
    {
        //a head of 10 px at x 20 to 30, from y 8, on a body that is wider under it
        var pixels = Figure(56, 80, new Rectangle(20, 8, 10, 12), new Rectangle(10, 20, 30, 50));

        Assert.Equal(new Rectangle(25 - 8, 8, 17, 17), AislingRenderer.HeadCrop(pixels, 56, 80, 17));
    }

    //a weapon low at the side of the figure does not move the square from the head
    [Fact]
    public void OnlyTheRowsOfTheSquareCentreIt()
    {
        var pixels = Figure(80, 80, new Rectangle(20, 8, 10, 12), new Rectangle(60, 40, 15, 20));

        Assert.Equal(25 - 8, AislingRenderer.HeadCrop(pixels, 80, 80, 17)!.Value.X);
    }

    //the faint top edge of a hair or a hat is part of the figure
    [Fact]
    public void AFaintPixelIsPartOfTheFigure()
    {
        var alpha = Figure(56, 80, new Rectangle(20, 8, 10, 12));
        alpha[5 * 56 + 24] = 40;

        Assert.Equal(5, AislingRenderer.HeadCrop(alpha, 56, 80, 17)!.Value.Y);
    }

    [Fact]
    public void AHatMovesTheTopOfTheSquareUp()
    {
        var pixels = Figure(56, 80, new Rectangle(22, 3, 6, 5), new Rectangle(20, 8, 10, 12), new Rectangle(10, 20, 30, 50));

        Assert.Equal(3, AislingRenderer.HeadCrop(pixels, 56, 80, 17)!.Value.Y);
    }

    [Fact]
    public void AnEmptyImageHasNoHead() => Assert.Null(AislingRenderer.HeadCrop(new byte[56 * 80], 56, 80, 17));

    //a figure at the edge of the image gives a square past the edge; its part outside is clear
    [Fact]
    public void TheSquareCanGoPastTheEdgeOfTheImage()
    {
        var pixels = Figure(20, 30, new Rectangle(0, 0, 4, 10));

        Assert.Equal(new Rectangle(2 - 8, 0, 17, 17), AislingRenderer.HeadCrop(pixels, 20, 30, 17));
    }

    //the cut keeps the pixels of the square, and the part of it past the edge of the figure is clear
    [Fact]
    public void TheCutKeepsTheHeadAndIsClearPastTheEdge()
    {
        using var bitmap = new SKBitmap(20, 30);

        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);

            using var paint = new SKPaint { Color = SKColors.Red };
            canvas.DrawRect(0, 0, 4, 10, paint);
        }

        using var figure = SKImage.FromBitmap(bitmap);
        using var head = AislingRenderer.CutHead(figure, 17);
        using var cut = SKBitmap.FromImage(head!);

        //the square starts at x -6: the head is at x 6 to 9 of the cut, and the cut is clear left of it and under it
        Assert.Equal((17, 17), (cut.Width, cut.Height));
        Assert.Equal(SKColors.Red, cut.GetPixel(6, 0));
        Assert.Equal(SKColors.Red, cut.GetPixel(9, 9));
        Assert.Equal(0, cut.GetPixel(5, 0).Alpha);
        Assert.Equal(0, cut.GetPixel(6, 10).Alpha);
        Assert.Equal(0, cut.GetPixel(10, 0).Alpha);
    }

    [Fact]
    public void AFigureWithNoPixelHasNoCut()
    {
        using var bitmap = new SKBitmap(20, 30);

        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.Transparent);

        using var figure = SKImage.FromBitmap(bitmap);

        Assert.Null(AislingRenderer.CutHead(figure, 17));
    }

    [Theory]
    [InlineData((byte)41, "", "Level 41")]
    [InlineData((byte)41, "Priest", "Level 41 Priest")]
    public void TheSecondLineIsTheLevelAndTheClass(byte level, string className, string expected)
        => Assert.Equal(expected, HudPlayerPlate.DetailOf(level, className));

    [Fact]
    public void ThePlateShowsTheNameTheLevelAndTheClass()
    {
        var attrs = new PlayerAttributes();
        var hud = NewHud(attrs);

        hud.SetPlayerName("Angelique");
        attrs.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 41 } });

        Assert.Equal("Angelique", hud.Plate.PlayerName);
        Assert.Equal("Level 41", hud.Plate.Detail);

        hud.SetPlayerClass(" Priest ");
        Assert.Equal("Level 41 Priest", hud.Plate.Detail);

        attrs.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 42 } });
        Assert.Equal("Level 42 Priest", hud.Plate.Detail);
    }

    //the name is in the title colour, and the second line in the text colour
    [Fact]
    public void TheNameIsInTheTitleColour()
    {
        var plate = NewHud(new PlayerAttributes()).Plate;
        var name = plate.FindChild<UILabel>("name")!;
        var detail = plate.FindChild<UILabel>("detail")!;

        name.ApplyForegroundRole();
        detail.ApplyForegroundRole();

        Assert.Equal(DialogPalette.ForRole(ThemeRoles.TextTitle), name.DrawnForegroundColor);
        Assert.Equal(DialogPalette.ForRole(ThemeRoles.Text), detail.DrawnForegroundColor);
    }

    [Fact]
    public void ThePlateHasItsPlaceAndTakesTheMouse()
    {
        var hud = NewHud(new PlayerAttributes());
        var plate = hud.Plate;

        Assert.Equal("player_plate", plate.Name);
        Assert.Equal(MainHudLayout.PlayerPlate, new Rectangle(plate.X, plate.Y, plate.Width, plate.Height));
        Assert.True(plate.IsHitTestVisible);
        Assert.Null(plate.IconTexture);
    }

    //the world screen opens the status book on PlateClicked
    [Fact]
    public void ALeftClickOnThePlateRaisesPlateClicked()
    {
        var hud = NewHud(new PlayerAttributes());
        var clicks = 0;
        hud.PlateClicked += () => clicks++;

        hud.Plate.OnClick(new ClickEvent { Button = MouseButton.Right });
        Assert.Equal(0, clicks);

        hud.Plate.OnClick(new ClickEvent { Button = MouseButton.Left });
        Assert.Equal(1, clicks);
    }
}
