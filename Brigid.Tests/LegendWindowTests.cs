#region
using System.Reflection;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.Hud.Main.Windows;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Brigid.Screens;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-151 phase 3: the Legend window of the new HUD. It lists the legend marks of the self profile (with the
///     player's own invisible marks) at the full size of the content, asks for the icon of each mark by its own id, and opens from the book in the title row of
///     the Details window.
/// </summary>
[Collection("FontEngine")]
public class LegendWindowTests : IDisposable
{
    private const int CONTENT_WIDTH = 612;
    private const int CONTENT_HEIGHT = 311;

    private readonly List<IDisposable> Owned = [];

    public LegendWindowTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        foreach (var owned in Owned)
            owned.Dispose();
    }

    private static readonly LegendMarkEntry Warrior = new("Warrior of Mileth", Color.Red, 3, "war");
    private static readonly LegendMarkEntry Hidden = new("a flag", Color.Transparent, 1, "flag");
    private static readonly LegendMarkEntry PackOnly = new("Hero of the Dubhaimid", Color.Gold, 250, "hero");

    private (LegendWindowControl Window, SelfProfile Profile, List<byte> Asked) NewWindow(params LegendMarkEntry[] marks)
    {
        var profile = new SelfProfile();
        profile.Update("Wizard", "", "", "", "Mileth", marks.ToList());
        List<byte> asked = [];
        var window = new LegendWindowControl(
            profile,
            id =>
            {
                asked.Add(id);

                return null;
            });
        window.SetBounds(new Rectangle(0, 0, CONTENT_WIDTH, CONTENT_HEIGHT));
        Owned.Add(window);

        return (window, profile, asked);
    }

    //the list binds its rows when it draws, so the test binds the rows as the list does
    private static LegendMarkRow[] Bind(LegendWindowControl window)
    {
        var rows = window.Rows.ToArray();

        for (var i = 0; i < rows.Length; i++)
            window.BindRow(
                rows[i],
                i < window.Marks.Count
                    ? new VirtualRow<LegendMarkEntry> { Kind = VirtualRowKind.Item, Item = window.Marks[i], Index = i }
                    : new VirtualRow<LegendMarkEntry> { Kind = VirtualRowKind.Empty });

        return rows;
    }

    [Fact]
    public void TheListFillsTheContent()
    {
        var (window, _, _) = NewWindow(Warrior);
        var list = window.ListView!;

        Assert.Equal(new Rectangle(0, 0, CONTENT_WIDTH, CONTENT_HEIGHT), new Rectangle(list.X, list.Y, list.Width, list.Height));
        Assert.Equal(CONTENT_HEIGHT / LegendWindowControl.ROW_HEIGHT, list.VisibleRows);
        UiAssert.NamedTree(window);
    }

    [Fact]
    public void ARowShowsTheTextInTheColourOfTheMark()
    {
        var (window, _, _) = NewWindow(Warrior, PackOnly);

        var rows = Bind(window);

        Assert.Equal(["Warrior of Mileth", "Hero of the Dubhaimid"], rows.Where(row => row.Visible).Select(row => row.TextShown));
        Assert.Equal([Color.Red, Color.Gold], rows.Where(row => row.Visible).Select(row => row.ColorShown));
        Assert.All(rows.Skip(2), row => Assert.False(row.Visible));
        Assert.False(window.EmptyShown);
    }

    //other players do not see an invisible mark, but the player sees their own, in the disabled text colour
    [Fact]
    public void AnInvisibleMarkShowsInTheDisabledColour()
    {
        var (window, _, asked) = NewWindow(Warrior, Hidden, PackOnly);

        var rows = Bind(window);

        Assert.Equal([Warrior, Hidden, PackOnly], window.Marks);
        Assert.Equal("a flag", rows[1].TextShown);
        Assert.Equal(DialogPalette.ForRole(ThemeRoles.TextDisabled), rows[1].ColorShown);
        Assert.NotEqual(Color.Transparent, rows[1].ColorShown);
        Assert.Contains(Hidden.Icon, asked);
    }

    //the legacy tab read the icons of the frames of legends.epf only, so a pack icon past the last frame did not show
    [Fact]
    public void ARowAsksForTheIconOfItsOwnId()
    {
        var (window, _, asked) = NewWindow(Warrior, PackOnly);

        Bind(window);

        Assert.Equal([Warrior.Icon, PackOnly.Icon], asked);
    }

    [Fact]
    public void AnEmptyListSaysSo()
    {
        var (window, profile, _) = NewWindow();

        Assert.True(window.EmptyShown);

        profile.Update("Wizard", "", "", "", "Mileth", [Warrior]);

        Assert.False(window.EmptyShown);

        profile.Update("Wizard", "", "", "", "Mileth", []);

        Assert.True(window.EmptyShown);
    }

    //the window can open before the first self profile of the character arrives: it has no marks yet, not no marks
    [Fact]
    public void TheWindowDoesNotSayEmptyBeforeTheFirstProfile()
    {
        var profile = new SelfProfile();
        var window = new LegendWindowControl(profile, _ => null);
        Owned.Add(window);

        Assert.False(window.EmptyShown);

        profile.SetProfileText("text");
        Assert.False(window.EmptyShown);

        profile.Update("Wizard", "", "", "", "Mileth", []);
        Assert.True(window.EmptyShown);
    }

    private static int ScrollOf(LegendWindowControl window) => window.ListView!.Children.OfType<ScrollBarControl>().Single().Value;

    //a group action asks for a self profile, and each reply is a new list: the same marks keep the scroll, other marks
    //show from the top
    [Fact]
    public void OnlyOtherMarksResetTheScroll()
    {
        var marks = Enumerable.Range(0, 40).Select(i => new LegendMarkEntry($"mark {i}", Color.White, (byte)i, $"k{i}")).ToArray();
        var (window, profile, _) = NewWindow(marks);

        window.ListView!.OnMouseScroll(new MouseScrollEvent { Delta = -5 });
        var scrolled = ScrollOf(window);
        Assert.True(scrolled > 0);

        profile.SetProfileText("new text");
        Assert.Equal(scrolled, ScrollOf(window));

        profile.Update("Wizard", "", "", "", "Mileth", marks.ToList());
        Assert.Equal(scrolled, ScrollOf(window));

        profile.Update("Wizard", "", "", "", "Mileth", marks.Append(Warrior).ToList());
        Assert.Equal(0, ScrollOf(window));
        Assert.Equal(41, window.Marks.Count);
    }

    //the server's invisible colour is the one colour that the window shows as a hidden mark
    [Fact]
    public void TheInvisibleColourOfTheServerIsHidden()
    {
        Assert.True(new LegendMarkEntry("a flag", WorldScreen.MapMarkColor(MarkColor.Invisible)).IsHidden);
        Assert.False(Warrior.IsHidden);
    }

    [Fact]
    public void TheWindowStopsReadingTheProfileWhenItIsDisposed()
    {
        var profile = new SelfProfile();
        var window = new LegendWindowControl(profile, _ => null);
        window.Dispose();

        profile.Update("Wizard", "", "", "", "Mileth", [Warrior]);

        Assert.Empty(window.Marks);
    }

    //Legend is a large panel with no button: the book at the left end of the title row of Details opens it
    [Fact]
    public void TheBookInTheTitleRowOfDetailsOpensLegend()
    {
        var hud = new MainHudControl(DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>(), attributes: new PlayerAttributes(), chat: new Chat());
        Owned.Add(hud);
        var details = hud.FindChild<HudPanel>("details_panel")!;
        var legend = hud.FindChild<HudPanel>("legend_panel")!;
        var book = details.FindChild<IconButton>("link_legend")!;
        var title = details.FindChild<UILabel>("title")!;

        Assert.Equal(ThemeRoles.IconPanelLegend, book.Role);
        Assert.Equal((HudPanel.PADDING, details.FindChild<TextButton>("close")!.Y), (book.X, book.Y));
        Assert.True(book.X + book.Width <= title.X);
        Assert.Contains(HudWindows.Legend, HudWindows.Unlisted);
        Assert.Equal(HudPanelSize.Large, HudWindows.Legend.Panel);
        Assert.DoesNotContain(HudWindows.Legend, HudWindows.All);

        hud.Windows.Toggle(HudWindows.Details);
        var tooltip = hud.FindChild<HudTooltip>("hud_tooltip")!;
        book.OnMouseEnter();
        Assert.True(tooltip.Visible);
        Assert.Contains("Legend", tooltip.WrappedText);
        Assert.True(tooltip.Y >= book.ScreenY + book.Height);

        book.OnClick(new ClickEvent { Button = MouseButton.Left });

        //a large panel closes the compact panels, and the book under the cursor goes with Details, so its tooltip goes too
        Assert.True(legend.Visible);
        Assert.False(details.Visible);
        Assert.False(tooltip.Visible);
        Assert.Equal(MainHudLayout.LargePanel, legend.Bounds);
        Assert.Equal(new Rectangle(0, 0, CONTENT_WIDTH, CONTENT_HEIGHT) with { X = HudPanel.PADDING, Y = legend.ContentBounds.Y }, legend.ContentBounds);
    }

    //a title link shows the icon of its window, which every host shows, so each one has built-in art
    [Fact]
    public void EachPanelIconHasBuiltInArt()
    {
        var links = HudWindows.All.Concat(HudWindows.Unlisted).Select(window => window.TitleLink).OfType<HudWindow>().ToArray();

        Assert.NotEmpty(links);
        Assert.All(links, link => Assert.Contains(link.LinkRole, ThemeRoles.PanelIcons));
        Assert.All(ThemeRoles.PanelIcons, role => Assert.True(BuiltInArt.Has(role), role));
    }

    [Fact]
    public void OnlyDetailsHasATitleLink()
    {
        var hud = new MainHudControl(DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>(), attributes: new PlayerAttributes(), chat: new Chat());
        Owned.Add(hud);

        Assert.Equal(["details_panel"], hud.Windows.Panels.Where(panel => panel.LinkButton is not null).Select(panel => panel.Name));
    }
}
