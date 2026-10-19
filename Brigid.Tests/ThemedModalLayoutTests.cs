#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Popups;
using Brigid.Controls.World.Popups.Boards;
using Brigid.Controls.World.Popups.Options;
using Brigid.Data.AssetPacks;
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

    private readonly IAssetPack? PreviousTheme = AssetPackRegistry.GetUiThemePack();
    private readonly UiThemePack Theme;

    public ThemedModalLayoutTests()
    {
        FontEngine.Initialize(0);
        Theme = UiThemeTests.BuildPack(
            """{ "density": 4, "parts": { "frame.panel": { "file": "frame.png", "slice": [44, 44, 44, 44] } } }""",
            ("frame.png", UiThemeTests.Png(128, 128)));
        AssetPackRegistry.SetPackForTests("ui_theme", Theme);
    }

    public void Dispose()
    {
        AssetPackRegistry.SetPackForTests("ui_theme", PreviousTheme);
        Theme.Dispose();
    }

    //Options is left out: its setting labels read msg.tbl, which a test does not load. It lays out from ContentBounds as
    //the others do
    public static TheoryData<string> Modals => ["boards", "castable", "keybind", "serverinfo", "certificate", "insecure"];

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

    //the content area of the same modal with no theme: a themed modal grows so that its content keeps this size
    private Point FlatContentSize(string modal)
    {
        AssetPackRegistry.SetPackForTests("ui_theme", null);

        try
        {
            return Create(modal).ContentBounds.Size;
        } finally
        {
            AssetPackRegistry.SetPackForTests("ui_theme", Theme);
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
        _             => throw new ArgumentOutOfRangeException(nameof(modal))
    };
}
