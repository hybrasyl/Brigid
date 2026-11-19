#region
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.Hud.Main.Windows;
using Brigid.Definitions;
using Brigid.Data.Models;
using Brigid.Rendering;
using Brigid.ViewModel;
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-149: the Abilities window. The rows are the skills and then the spells, each in the order of its book slots,
///     and after the learned rows of a kind the abilities of the class metadata that the book does not have; the filters
///     choose the learned and the unlearned rows; a page holds 16 rows, down the left column and then the right; the list
///     builds again after a change and keeps its page.
/// </summary>
[Collection("FontEngine")]
public class AbilitiesWindowTests : IDisposable
{
    public AbilitiesWindowTests() => FontEngine.Initialize(0);

    private readonly List<AbilitiesWindowControl> Windows = [];
    private readonly List<IDisposable> Owned = [];

    public void Dispose()
    {
        foreach (var window in Windows)
            window.Dispose();

        foreach (var owned in Owned)
            owned.Dispose();
    }

    private static AbilityMetadataEntry Entry(string name, bool isSpell, ushort icon = 1) => new() { Name = name, IsSpell = isSpell, IconSprite = icon };

    private static AbilityMetadata Metadata(AbilityMetadataEntry[] skills, AbilityMetadataEntry[] spells) => new(skills, spells);

    private static string[] Names(IEnumerable<AbilityRow> rows) => rows.Select(row => row.Name).ToArray();

    private static AbilityIconState Locked(AbilityMetadataEntry entry) => AbilityIconState.Locked;

    [Fact]
    public void TheSkillsComeFirstThenTheSpellsEachInTheOrderOfItsSlots()
    {
        var skills = new SkillBook();
        skills.SetSlot(40, 1, "Kick");
        skills.SetSlot(2, 1, "Assail (Lev:3/100)");
        var spells = new SpellBook();
        spells.SetSlot(5, 1, "beag ioc");
        spells.SetSlot(1, 1, "ard ioc");

        var rows = AbilityList.Build(skills, spells, null, true, true, Locked);

        Assert.Equal(["Assail", "Kick", "ard ioc", "beag ioc"], Names(rows));
        Assert.Equal([(byte)2, (byte)40, (byte)1, (byte)5], rows.Select(row => row.Slot!.Value).ToArray());
        Assert.Equal([false, false, true, true], rows.Select(row => row.IsSpell).ToArray());
    }

    [Fact]
    public void TheUnlearnedRowsComeAfterTheLearnedRowsOfTheirKind()
    {
        var skills = new SkillBook();
        skills.SetSlot(1, 1, "Assail");
        var spells = new SpellBook();
        spells.SetSlot(1, 1, "beag ioc");
        var metadata = Metadata([Entry("assail", false), Entry("Wolf Fang Fist", false)], [Entry("Beag Ioc", true), Entry("ard ioc", true)]);

        var rows = AbilityList.Build(skills, spells, metadata, true, true, Locked);

        Assert.Equal(["Assail", "Wolf Fang Fist", "beag ioc", "ard ioc"], Names(rows));
        Assert.Equal([true, false, true, false], rows.Select(row => row.IsLearned).ToArray());
        Assert.Same(metadata.Skills[0], rows[0].Entry);
        Assert.Same(metadata.Skills[1], rows[1].Entry);
    }

    [Theory]
    [InlineData(true, false, new[] { "Assail" })]
    [InlineData(false, true, new[] { "Kick" })]
    [InlineData(false, false, new string[0])]
    public void TheFiltersChooseTheRows(bool learned, bool unlearned, string[] expected)
    {
        var skills = new SkillBook();
        skills.SetSlot(1, 1, "Assail");
        var metadata = Metadata([Entry("Assail", false), Entry("Kick", false)], []);

        Assert.Equal(expected, Names(AbilityList.Build(skills, new SpellBook(), metadata, learned, unlearned, Locked)));
    }

    [Theory]
    [InlineData((byte)3, (byte)100, "3/100")]
    [InlineData((byte)0, (byte)0, "")]
    public void TheLevelTextIsTheLevelOfTheMaximum(byte level, byte max, string expected)
        => Assert.Equal(expected, AbilityList.LevelText(level, max));

    [Theory]
    [InlineData(0, 1)]
    [InlineData(16, 1)]
    [InlineData(17, 2)]
    [InlineData(48, 3)]
    public void APageHoldsSixteenRows(int count, int pages) => Assert.Equal(pages, AbilityList.PageCount(count));

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(7, 0, 7)]
    [InlineData(8, 1, 0)]
    [InlineData(15, 1, 7)]
    public void ARowGoesDownTheLeftColumnThenTheRight(int index, int column, int row)
        => Assert.Equal((column, row), AbilityList.PlaceOf(index));

    //the content of a compact panel, and a smaller content
    private static readonly Rectangle FlatContent = new(6, 22, 298, 311);
    private static readonly Rectangle SmallContent = new(14, 30, 282, 295);

    private AbilitiesWindowControl NewWindow(
        SkillBook skills,
        SpellBook spells,
        AbilityMetadata? metadata = null,
        PlayerAttributes? attributes = null,
        Func<AbilityMetadataEntry, AbilityIconState>? stateOf = null)
    {
        var window = new AbilitiesWindowControl(skills, spells, attributes ?? new PlayerAttributes(), () => metadata, _ => null, stateOf ?? Locked);
        window.SetBounds(FlatContent);
        Windows.Add(window);
        window.Refresh();

        return window;
    }

    private static void Click(UIElement element) => element.OnClick(new ClickEvent { Button = MouseButton.Left });

    private static SkillBook Skills(int count)
    {
        var book = new SkillBook();

        for (byte slot = 1; slot <= count; slot++)
            book.SetSlot(slot, 1, $"Skill {slot}");

        return book;
    }

    [Fact]
    public void TheWindowShowsAPageAndItsNumber()
    {
        var window = NewWindow(Skills(20), new SpellBook());

        Assert.Equal("1 / 2", window.PageText);
        Assert.Equal("Skill 1", window.RowControls[0].NameShown);
        Assert.Equal("Skill 9", window.RowControls[8].NameShown);
        Assert.True(window.RowControls[15].Visible);

        window.TurnPage(1);

        Assert.Equal("2 / 2", window.PageText);
        Assert.Equal("Skill 17", window.RowControls[0].NameShown);
        Assert.False(window.RowControls[4].Visible);

        window.TurnPage(1);
        Assert.Equal(1, window.Page);

        window.TurnPage(-5);
        Assert.Equal(0, window.Page);
    }

    [Fact]
    public void AChangeBuildsTheListAgainAndKeepsThePage()
    {
        var skills = Skills(20);
        var window = NewWindow(skills, new SpellBook());
        window.TurnPage(1);

        skills.SetSlot(30, 1, "Kick (Lev:2/50)");
        Assert.Equal(20, window.Entries.Count);

        window.Refresh();

        Assert.Equal(21, window.Entries.Count);
        Assert.Equal(1, window.Page);
        Assert.Equal("Kick", window.RowControls[4].NameShown);
        Assert.Equal("2/50", window.RowControls[4].LevelShown);
    }

    [Fact]
    public void AClearOfABookEmptiesTheListAndClampsThePage()
    {
        var skills = Skills(20);
        var window = NewWindow(skills, new SpellBook());
        window.TurnPage(1);

        skills.Clear();
        window.Refresh();

        Assert.Empty(window.Entries);
        Assert.Equal(0, window.Page);
        Assert.Equal("1 / 1", window.PageText);
        Assert.All(window.RowControls, row => Assert.False(row.Visible));
    }

    [Fact]
    public void TheMetadataBuildsTheListAgainWhenItArrives()
    {
        AbilityMetadata? metadata = null;
        var window = new AbilitiesWindowControl(new SkillBook(), new SpellBook(), new PlayerAttributes(), () => metadata, _ => null, Locked);
        Windows.Add(window);
        window.Refresh();
        Click(window.UnlearnedFilter);
        window.Refresh();
        Assert.Empty(window.Entries);

        metadata = Metadata([Entry("Kick", false)], []);
        window.Refresh();

        Assert.Equal(["Kick"], Names(window.Entries));
    }

    //a click on a filter builds the list again; the window does not wait for another change
    [Fact]
    public void AClickOnAFilterBuildsTheListAgain()
    {
        var skills = Skills(2);
        var window = NewWindow(skills, new SpellBook(), Metadata([Entry("Kick", false)], []));
        Assert.Equal(2, window.Entries.Count);

        Click(window.UnlearnedFilter);
        window.Refresh();
        Assert.Equal(3, window.Entries.Count);

        Click(window.LearnedFilter);
        window.Refresh();
        Assert.Equal(["Kick"], Names(window.Entries));
    }

    //the blue or grey icon of an ability that the player can learn follows the attributes
    [Fact]
    public void AChangeOfTheAttributesShowsTheNewStateOfAnUnlearnedAbility()
    {
        var attributes = new PlayerAttributes();
        var state = AbilityIconState.Locked;
        var window = NewWindow(new SkillBook(), new SpellBook(), Metadata([Entry("Kick", false)], []), attributes, _ => state);
        Click(window.UnlearnedFilter);
        window.Refresh();
        Assert.Equal(AbilityIconState.Locked, window.Entries[0].State);

        state = AbilityIconState.Learnable;
        attributes.Update(new DALib.Networking.Packets.Server.AttributesPacket { Primary = new DALib.Networking.Packets.Server.PrimaryAttributes { Level = 10 } });
        window.Refresh();

        Assert.Equal(AbilityIconState.Learnable, window.Entries[0].State);
        Assert.Equal(AbilityIconState.Learnable, window.RowControls[0].Row!.State);
    }

    //the vitals change often; with no unlearned rows, the attributes do not build the list again
    [Fact]
    public void WithNoUnlearnedRowsTheAttributesDoNotBuildTheListAgain()
    {
        var attributes = new PlayerAttributes();
        var builds = 0;
        var window = NewWindow(Skills(1), new SpellBook(), Metadata([Entry("Kick", false)], []), attributes, _ =>
        {
            builds++;

            return AbilityIconState.Locked;
        });

        var before = window.Entries;
        attributes.Update(new DALib.Networking.Packets.Server.AttributesPacket { Current = new DALib.Networking.Packets.Server.CurrentAttributes { Hp = 5 } });
        window.Refresh();

        Assert.Equal(0, builds);
        Assert.Same(before, window.Entries);
    }

    [Fact]
    public void TheFiltersStartWithTheLearnedRows()
    {
        var window = NewWindow(new SkillBook(), new SpellBook());

        Assert.True(window.LearnedFilter.Checked);
        Assert.False(window.UnlearnedFilter.Checked);
    }

    [Fact]
    public void TheTraitsTabIsAPlaceholder()
    {
        var window = NewWindow(Skills(3), new SpellBook());

        window.SelectTab(1);

        Assert.True(window.ShowsTraits);
        Assert.All(window.RowControls, row => Assert.False(row.Visible));
        Assert.False(window.LearnedFilter.Visible);
        Assert.False(window.UnlearnedFilter.Visible);
        Assert.False(window.FindChild<UIElement>("page_number")!.Visible);
        Assert.False(window.FindChild<UIElement>("next_page")!.Visible);

        window.SelectTab(0);
        Assert.True(window.RowControls[0].Visible);
    }

    //the flat content is the content of a compact panel
    [Fact]
    public void TheFlatContentIsTheContentOfACompactPanel()
    {
        var panel = new HudPanel(HudWindows.Abilities);
        Owned.Add(panel);

        Assert.Equal(FlatContent, panel.ContentBounds);
    }

    //every part is inside the content, and the parts of a line do not overlap, in the content of a panel and in a smaller one
    [Theory]
    [InlineData(298, 311)]
    [InlineData(282, 295)]
    public void ThePartsFitTheContentAndDoNotOverlap(int width, int height)
    {
        var layout = MainHudLayout.AbilitiesIn(width, height);
        var content = new Rectangle(0, 0, width, height);
        var header = new[] { layout.Tabs, layout.PreviousPage, layout.PageNumber, layout.NextPage };
        var filters = new[] { layout.UnlearnedBox, layout.UnlearnedLabel, layout.LearnedBox, layout.LearnedLabel };
        var rows = Enumerable.Range(0, AbilityList.PAGE).Select(i => AbilityList.PlaceOf(i)).Select(place => layout.RowAt(place.Column, place.Row)).ToArray();

        foreach (var part in header.Concat(filters).Concat(rows).Append(layout.Traits))
            Assert.True(content.Contains(part), $"{part} is not in {content}");

        foreach (var group in new[] { header, filters, rows })
            for (var i = 0; i < group.Length; i++)
                for (var j = i + 1; j < group.Length; j++)
                    Assert.False(group[i].Intersects(group[j]), $"{group[i]} and {group[j]} overlap");

        Assert.True(header.Max(part => part.Bottom) <= filters.Min(part => part.Top));
        Assert.True(filters.Max(part => part.Bottom) <= rows.Min(part => part.Top));
        Assert.True(layout.RowPitch >= MainHudLayout.ABILITY_ICON);
    }

    //the panel gives the content its bounds, and the window lays out in them
    [Fact]
    public void TheWindowLaysOutInTheBoundsThatItGets()
    {
        var window = NewWindow(Skills(16), new SpellBook());
        window.SetBounds(SmallContent);

        var layout = MainHudLayout.AbilitiesIn(SmallContent.Width, SmallContent.Height);
        var last = window.RowControls[^1];

        Assert.Equal(layout.RowAt(1, 7), new Rectangle(last.X, last.Y, last.Width, last.Height));
        Assert.Equal(layout.NextPage.X, window.FindChild<UIElement>("next_page")!.X);

        var name = last.FindChild<UIElement>("name")!;
        Assert.Equal(AbilitiesLayout.NameIn(last.Width), new Rectangle(name.X, name.Y, name.Width, name.Height));
    }

    [Fact]
    public void TheWindowStopsListeningWhenItIsDisposed()
    {
        var skills = new SkillBook();
        var window = new AbilitiesWindowControl(skills, new SpellBook(), new PlayerAttributes(), () => null, _ => null, Locked);
        window.Refresh();
        window.Dispose();

        skills.SetSlot(1, 1, "Assail");
        window.Refresh();

        Assert.Empty(window.Entries);
    }
}
