#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Controls.World.Popups;
using Brigid.Controls.World.Popups.Boards;
using Brigid.Controls.World.Popups.Dialog;
using Brigid.Controls.World.Popups.Options;
using Brigid.Controls.World.Popups.Profile;
using Brigid.Data.AssetPacks;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-120: with a theme whose frame art has a wide border (11 px, as the icy-steel frame has), every world modal and
///     connection prompt lays out its content inside the frame, and keeps its bottom bar below the content.
/// </summary>
[Collection("FontEngine")]
public sealed class ThemedModalLayoutTests : IDisposable
{
    //44 art px at density 4 is an 11 px border; FramePanel then insets the content 14 px
    private const int THEMED_INSET = 14;
    private const int FLAT_PADDING = 6;

    private readonly UiThemePack Theme;

    public ThemedModalLayoutTests()
    {
        FontEngine.Initialize(0);
        Theme = UiThemeTests.BuildPack(
            """{ "density": 4, "parts": { "frame.panel": { "file": "frame.png", "slice": [44, 44, 44, 44] } } }""",
            ("frame.png", UiThemeTests.Png(128, 128)));
        AssetPackRegistry.SetUiThemeForTests(Theme);
    }

    public void Dispose()
    {
        AssetPackRegistry.SetUiThemeForTests(null);
        Theme.Dispose();
    }

    //Options is left out: its setting labels read msg.tbl, which a test does not load. It lays out from ContentBounds as
    //the others do
    public static TheoryData<string> Modals => ["boards", "castable", "keybind", "serverinfo", "certificate", "insecure", "pause", "popup", "itemamount", "goldamount", "textpopup", "profiletext", "abilitydetails", "eventdetails"];

    [Theory]
    [MemberData(nameof(Modals))]
    public void TheContentLiesInsideTheThemedFrame(string modal)
    {
        var panel = Create(modal);
        var content = panel.ContentBounds;

        Assert.Equal(THEMED_INSET, content.X);
        Assert.Equal(FlatContentSize(modal), content.Size);
        Assert.True(content.Bottom <= panel.BottomBarTop);

        foreach (var child in panel.Children)
        {
            //the title band and the bottom bar are the frame's own chrome
            if (child.Name == "title" || (child is TextButton && child.Y >= panel.BottomBarTop))
                continue;

            Assert.True(child.X >= content.X, $"{modal}: {child.Name} starts left of the content at x={child.X}");
            Assert.True(child.Y >= content.Y, $"{modal}: {child.Name} starts above the content at y={child.Y}");
            Assert.True(child.X + child.Width <= content.Right, $"{modal}: {child.Name} ends right of the content");
            Assert.True(child.Y + child.Height <= content.Bottom, $"{modal}: {child.Name} ends below the content");
        }
    }

    [Fact]
    public void TheTextPopupSignKeepsTheRetailSizeInsideAThemedFrame()
    {
        var popup = new TextPopupControl();
        var scroll = popup.FindChild<ScrollView>("text")!;
        var grow = 2 * (THEMED_INSET - FLAT_PADDING);

        popup.Show("Framed.");

        Assert.Equal(360 + grow, popup.Width);
        Assert.Equal(180 + grow, popup.Height);

        popup.Show("A sign.", PopupStyle.Wooden);

        //the sign is the retail 360 by 180, with the text 16 px in at the sides, 12 px from the top and 32 px from the bottom
        Assert.Equal(360, popup.Width);
        Assert.Equal(180, popup.Height);
        Assert.Equal(new Rectangle(16, 12, 328, 136), new Rectangle(scroll.X, scroll.Y, scroll.Width, scroll.Height));
        Assert.Equal((ChaosGame.VIRTUAL_WIDTH - 360) / 2, popup.X);

        popup.Show("Framed again.");

        Assert.Equal(360 + grow, popup.Width);
        Assert.Equal((ChaosGame.VIRTUAL_WIDTH - popup.Width) / 2, popup.X);
        Assert.True(scroll.Y >= popup.ContentBounds.Y);
        Assert.True(scroll.Y + scroll.Height <= popup.ContentBounds.Bottom);
    }

    [Fact]
    public void TheBankShopPanelGrowsAroundItsContent()
    {
        AssetPackRegistry.SetUiThemeForTests(null);
        var flat = new BankShopPanel();
        AssetPackRegistry.SetUiThemeForTests(Theme);

        var themed = new BankShopPanel();
        var grow = THEMED_INSET - FLAT_PADDING;

        Assert.Equal(flat.Width + 2 * grow, themed.Width);
        Assert.Equal(flat.Height + 2 * grow, themed.Height);

        for (var i = 0; i < themed.Children.Count; i++)
        {
            var child = themed.Children[i];

            //every child keeps its flat place, moved in by the growth, so it clears the frame art
            Assert.Equal(flat.Children[i].X + grow, child.X);
            Assert.Equal(flat.Children[i].Y + grow, child.Y);
            //content that sat at the flat padding now sits at the themed inset; the full-width title band is the exception
            if (flat.Children[i].X >= FLAT_PADDING)
                Assert.True(child.X >= THEMED_INSET, $"{child.Name} starts under the frame at x={child.X}");
        }
    }

    [Fact]
    public void GrowAroundContentMovesTheChildrenInAndRunsOnce()
    {
        var themed = new GrowingPanel(themed: true);

        Assert.Equal(THEMED_INSET - FLAT_PADDING, themed.Offset);
        Assert.Equal(100 + 2 * themed.Offset, themed.Width);
        Assert.Equal(50 + 2 * themed.Offset, themed.Height);
        Assert.Equal(THEMED_INSET, themed.Children[0].X);
        Assert.Equal(THEMED_INSET, themed.Children[0].Y);
        Assert.Throws<InvalidOperationException>(themed.GrowAgain);

        var flat = new GrowingPanel(themed: false);

        Assert.Equal(0, flat.Offset);
        Assert.Equal(100, flat.Width);
        Assert.Equal(FLAT_PADDING, flat.Children[0].X);
    }

    //a panel that lays out one child at the flat padding, as the hand-framed world panels do
    private sealed class GrowingPanel : FramePanel
    {
        public int Offset { get; }

        public GrowingPanel(bool themed)
        {
            UseTheme = themed;
            Width = 100;
            Height = 50;
            AddChild(new UIPanel { X = FLAT_PADDING, Y = FLAT_PADDING, Width = 10, Height = 10 });
            Offset = GrowAroundContent(FLAT_PADDING);
        }

        public void GrowAgain() => GrowAroundContent(FLAT_PADDING);
    }

    //the content area of the same modal with no theme: a themed modal grows so that its content keeps this size
    private Point FlatContentSize(string modal)
    {
        AssetPackRegistry.SetUiThemeForTests(null);

        try
        {
            return Create(modal).ContentBounds.Size;
        } finally
        {
            AssetPackRegistry.SetUiThemeForTests(Theme);
        }
    }

    private static CenteredModalPanel Create(string modal) => modal switch
    {
        "boards"      => new BoardsModalControl(),
        "castable"    => new CastablePopupControl(),
        "keybind"     => new KeybindCaptureControl(),
        "serverinfo"  => new ServerInfoControl(),
        "certificate" => new CertificateTrustPromptControl(),
        "insecure"    => new InsecureConnectionPromptControl(),
        "pause"       => new PauseMenuControl(),
        "popup"       => new OkPopupMessageControl("Notice", showCancel: true),
        "itemamount"  => new ItemAmountControl(),
        "goldamount"  => new GoldAmountControl(),
        "textpopup"   => new TextPopupControl(),
        "profiletext" => new SelfProfileTextEditorControl(),
        "abilitydetails" => new AbilityMetadataDetailsControl(),
        "eventdetails" => new EventMetadataDetailsControl(),
        _             => throw new ArgumentOutOfRangeException(nameof(modal))
    };
}
