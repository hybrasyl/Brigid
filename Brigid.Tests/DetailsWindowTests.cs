#region
using System.Reflection;
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Hud;
using Brigid.Controls.World.Hud.Main;
using Brigid.Controls.World.Hud.Main.Windows;
using Brigid.Controls.World.Popups.Profile;
using Brigid.Definitions;
using Brigid.Data.Models;
using Brigid.Models;
using Brigid.Rendering;
using Brigid.ViewModel;
using Chaos.DarkAges.Definitions;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-151: the Details window of the new HUD. The layout fits the content; the facts follow the self profile, with
///     "Human" for the species and a disabled title drop-down; the stat raise holds pending points until Confirm; the
///     Activity Log shows the session's gains; G opens the window, which has no button.
/// </summary>
[Collection("FontEngine")]
public class DetailsWindowTests : IDisposable
{
    private const int FLAT_WIDTH = 298;
    private const int FLAT_HEIGHT = 311;

    private readonly List<IDisposable> Owned = [];

    public DetailsWindowTests() => FontEngine.Initialize(0);

    public void Dispose()
    {
        foreach (var owned in Owned)
            owned.Dispose();
    }

    private static AttributesPacket Primary(byte str = 3, byte intel = 4, byte wis = 5, byte con = 6, byte dex = 7, byte unspent = 0, byte level = 10)
        => new()
        {
            Primary = new PrimaryAttributes
            {
                Level = level,
                Str = str,
                Int = intel,
                Wis = wis,
                Con = con,
                Dex = dex,
                UnspentPoints = unspent
            }
        };

    private long Now;

    private (DetailsWindowControl Window, SelfProfile Profile, PlayerAttributes Attributes, ActivityLog Activity) NewWindow(byte unspent = 0)
    {
        var profile = new SelfProfile();
        var attributes = new PlayerAttributes();
        var activity = new ActivityLog(attributes, profile, () => new DateTime(2026, 11, 18, 9, 5, 0));
        attributes.Update(Primary(unspent: unspent));

        var window = new DetailsWindowControl(profile, attributes, activity, () => Now);
        window.SetBounds(new Rectangle(6, 22, FLAT_WIDTH, FLAT_HEIGHT));
        Owned.Add(window);

        return (window, profile, attributes, activity);
    }

    //the window builds the arrows before it lays out, so an arrow's icon must follow the bounds that the layout gives it
    [Fact]
    public void EachArrowShowsItsIconAtTheSizeOfTheLayout()
    {
        var (window, _, _, _) = NewWindow();
        var layout = MainHudLayout.DetailsIn(FLAT_WIDTH, FLAT_HEIGHT);

        foreach (var (prefix, role, place) in new (string, string, Func<int, Rectangle>)[]
                 {
                     ("raise", ThemeRoles.IconDetailsRaise, layout.Raise),
                     ("lower", ThemeRoles.IconDetailsLower, layout.Lower)
                 })
            for (var i = 0; i < StatRaise.Stats.Length; i++)
            {
                var arrow = window.FindChild<IconButton>($"{prefix}_{StatRaise.Stats[i].ToString().ToLowerInvariant()}")!;
                var icon = arrow.FindChild<RoleIcon>("icon")!;

                Assert.Equal(role, arrow.Role);
                Assert.Equal(place(i).Size, new Point(arrow.Width, arrow.Height));
                Assert.Equal(new Rectangle(2, 2, arrow.Width - 4, arrow.Height - 4), new Rectangle(icon.X, icon.Y, icon.Width, icon.Height));
            }

        Assert.All(ThemeRoles.DetailsIcons, role => Assert.True(BuiltInArt.Has(role), role));
        UiAssert.NamedTree(window);
    }

    [Fact]
    public void ThePartsFitTheContentAndDoNotOverlap()
    {
        var layout = MainHudLayout.DetailsIn(FLAT_WIDTH, FLAT_HEIGHT);
        var content = new Rectangle(0, 0, FLAT_WIDTH, FLAT_HEIGHT);
        List<Rectangle> parts = [.. layout.Labels, .. layout.Values, layout.Portrait, layout.ProfileText, layout.Stats, layout.Activity];

        Assert.All(parts, part => Assert.True(content.Contains(part), part.ToString()));

        for (var i = 0; i < parts.Count; i++)
            for (var j = i + 1; j < parts.Count; j++)
                Assert.False(parts[i].Intersects(parts[j]), $"{parts[i]} and {parts[j]}");

        //the stat rows, the points and Confirm are in the stat box, and the rows end above Confirm
        for (var i = 0; i < StatRaise.Stats.Length; i++)
        {
            Assert.True(layout.Stats.Contains(layout.StatLabel(i)));
            Assert.True(layout.Stats.Contains(layout.Lower(i)));
            Assert.False(layout.StatValue(i).Intersects(layout.Raise(i)));
            Assert.True(layout.Raise(i).Right <= layout.Lower(i).Left);
        }

        Assert.True(layout.Stats.Contains(layout.Confirm));
        Assert.True(layout.Stats.Contains(layout.Remaining));
        Assert.True(layout.Lower(StatRaise.Stats.Length - 1).Bottom <= layout.Confirm.Top);
        Assert.Equal(new Point(MainHudLayout.DETAILS_PORTRAIT, MainHudLayout.DETAILS_PORTRAIT), layout.Portrait.Size);
    }

    //the editor takes exactly the text that the Details window shows
    [Fact]
    public void TheEditorHasTheSizeOfTheProfileTextBox()
    {
        var layout = MainHudLayout.DetailsIn(FLAT_WIDTH, FLAT_HEIGHT);
        var editor = new SelfProfileTextEditorControl();
        Owned.Add(editor);
        var box = editor.FindChild<UITextBox>("text")!;

        Assert.Equal(layout.ProfileText.Size, new Point(box.Width, box.Height));
    }

    [Fact]
    public void TheFactsFollowTheProfileWithHumanForTheSpecies()
    {
        var (window, profile, _, _) = NewWindow();

        profile.SetName("Sabrael");
        profile.Update(" Wizard ", "The Guild", "Officer", "Lord", "Mileth", []);

        Assert.Equal(["Sabrael", "Wizard", "Human", "The Guild", "Officer", "Mileth"], Enumerable.Range(0, 6).Select(window.FieldShown));
        Assert.Equal("Lord", window.TitleBox.SelectedText);
        Assert.False(window.TitleBox.Enabled);

        profile.Update("Wizard", "The Guild", "Officer", "Duke", "Mileth", []);
        Assert.Equal(["Duke"], window.TitleBox.Entries);
        Assert.Single(window.Children, child => child.Name == "title");
    }

    [Fact]
    public void TheProfileTextShowsAndAClickAsksForTheEditor()
    {
        var (window, profile, _, _) = NewWindow();
        var asked = 0;
        window.ProfileTextRequested += () => asked++;

        profile.SetProfileText("Hello.");
        window.FindChild<ProfileTextBox>("profile_text")!.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.Equal("Hello.", window.ProfileTextShown);
        Assert.Equal(1, asked);
    }

    //a text saved before the editor took this size can have more lines than the box shows: the wheel scrolls it
    [Fact]
    public void TheWheelScrollsALongProfileText()
    {
        var box = new ProfileTextBox();
        Owned.Add(box);
        box.SetBounds(new Rectangle(0, 0, MainHudLayout.DETAILS_PROFILE_WIDTH, MainHudLayout.DETAILS_PROFILE_HEIGHT));
        box.Text = string.Join('\n', Enumerable.Range(1, 12).Select(i => $"line {i}"));
        box.Update(new GameTime());

        box.OnMouseScroll(new MouseScrollEvent { Delta = -3 });
        Assert.Equal(3, box.FirstLineShown);

        //the last line stops at the bottom of the box, and the first at its top
        box.OnMouseScroll(new MouseScrollEvent { Delta = -20 });
        Assert.Equal(7, box.FirstLineShown);

        box.OnMouseScroll(new MouseScrollEvent { Delta = 20 });
        Assert.Equal(0, box.FirstLineShown);

        //a new text starts at its first line
        box.OnMouseScroll(new MouseScrollEvent { Delta = -2 });
        box.Text = "short";
        Assert.Equal(0, box.FirstLineShown);
    }

    [Fact]
    public void AnEmptyPortraitShowsNoTexture()
    {
        var (window, profile, _, _) = NewWindow();

        profile.SetPortrait([1, 2, 3]);

        Assert.Null(window.PortraitTexture);
    }

    [Fact]
    public void TheArrowsHoldPendingPointsUntilConfirm()
    {
        var (window, _, _, _) = NewWindow(unspent: 3);
        var raised = new List<Stat>();
        window.RaiseRequested += raised.Add;

        Assert.Equal("Points 3", window.RemainingShown);
        Assert.All(window.LowerControls, lower => Assert.False(lower.Enabled));
        Assert.False(window.ConfirmButton.Enabled);

        //DEX, STR, DEX: the raise sends them in the order of the rows
        Click(window.RaiseControls[4]);
        Click(window.RaiseControls[0]);
        Click(window.RaiseControls[4]);

        Assert.Equal("7 +2", window.StatShown(4));
        Assert.Equal("3 +1", window.StatShown(0));
        Assert.Equal("Points 0", window.RemainingShown);
        Assert.All(window.RaiseControls, raise => Assert.False(raise.Enabled));
        Assert.True(window.LowerControls[4].Enabled);
        Assert.False(window.LowerControls[1].Enabled);
        Assert.Empty(raised);

        Click(window.LowerControls[0]);
        Assert.Equal("3", window.StatShown(0));

        Click(window.ConfirmButton);

        Assert.Equal([Stat.DEX, Stat.DEX], raised);
        Assert.Equal("7", window.StatShown(4));
        Assert.False(window.ConfirmButton.Enabled);
    }

    [Fact]
    public void AFallOfTheUnspentPointsCutsTheNewestPendingPoints()
    {
        var (window, _, attributes, _) = NewWindow(unspent: 3);

        Click(window.RaiseControls[0]);
        Click(window.RaiseControls[1]);
        Click(window.RaiseControls[2]);

        attributes.Update(Primary(unspent: 1));

        Assert.Equal(1, window.StatPoints.PendingOf(Stat.STR));
        Assert.Equal(0, window.StatPoints.PendingOf(Stat.INT));
        Assert.Equal(0, window.StatPoints.PendingOf(Stat.WIS));
        Assert.Equal("Points 0", window.RemainingShown);
    }

    private static void Click(UIElement element) => element.OnClick(new ClickEvent { Button = MouseButton.Left });

    //the points of a Confirm count as spent until the server's unspent points show them, so a second Confirm sends no
    //more points than the player has; the wait ends when the server answers, or after the longest wait
    [Fact]
    public void ThePointsOfAConfirmWaitForTheServer()
    {
        var (window, _, attributes, _) = NewWindow(unspent: 2);

        Click(window.RaiseControls[0]);
        Click(window.RaiseControls[1]);
        Click(window.ConfirmButton);

        Assert.Equal("Points 0", window.RemainingShown);
        Assert.False(window.RaiseControls[0].Enabled);

        //one answer of the two
        attributes.Update(Primary(str: 4, unspent: 1));
        Assert.Equal("Points 0", window.RemainingShown);

        attributes.Update(Primary(str: 4, intel: 5, unspent: 0));
        Assert.Equal(0, window.StatPoints.Waiting);
        Assert.Equal("Points 0", window.RemainingShown);
    }

    [Fact]
    public void AConfirmThatTheServerDoesNotAnswerStopsWaiting()
    {
        var (window, _, _, _) = NewWindow(unspent: 2);

        Click(window.RaiseControls[0]);
        Click(window.ConfirmButton);
        Assert.Equal("Points 1", window.RemainingShown);

        Now += StatRaise.ANSWER_WAIT_MS;
        window.Update(new GameTime());

        Assert.Equal("Points 2", window.RemainingShown);
    }

    [Fact]
    public void AClosedWindowClearsThePendingPoints()
    {
        var (window, _, _, _) = NewWindow(unspent: 2);

        Click(window.RaiseControls[3]);
        window.ClearPending();

        Assert.False(window.StatPoints.HasPending);
        Assert.Equal("6", window.StatShown(3));
        Assert.Equal("Points 2", window.RemainingShown);
    }

    [Fact]
    public void TheStatRaiseRules()
    {
        var raise = new StatRaise();
        raise.SetUnspent(2);

        Assert.True(raise.Add(Stat.CON));
        Assert.True(raise.Add(Stat.STR));
        Assert.False(raise.Add(Stat.STR));
        Assert.False(raise.Remove(Stat.DEX));
        Assert.Equal(0, raise.Remaining);

        Assert.True(raise.Remove(Stat.CON));
        Assert.Equal(1, raise.Remaining);

        raise.SetUnspent(-4);
        Assert.Equal(0, raise.Unspent);
        Assert.False(raise.HasPending);

        raise.SetUnspent(5);
        raise.Add(Stat.WIS);
        raise.Add(Stat.STR);
        Assert.Equal([Stat.STR, Stat.WIS], raise.Confirm());
        Assert.False(raise.HasPending);
    }

    private static LegendMarkEntry Mark(string text, string key = "") => new(text, Color.White, 0, key);

    [Fact]
    public void TheActivityLogShowsTheSessionsGainsNewestFirst()
    {
        var (window, profile, attributes, activity) = NewWindow();

        //the first readings only set the start: the window's attributes set level 10
        profile.Update("Wizard", "", "", "", "", [Mark("Born")]);
        Assert.Empty(activity.Entries);

        attributes.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 12, Ability = 1 } });
        profile.Update("Wizard", "", "", "", "", [Mark("Born"), Mark("Slew a dragon")]);

        Assert.Equal(["Legend: Slew a dragon", "Reached ability level 1", "Reached level 12 (+2)"], activity.Entries.Select(entry => entry.Text));
        Assert.Equal("09:05 Legend: Slew a dragon", window.ActivityShown[0]);
        Assert.Equal(3, window.ActivityShown.Count);

        //a lower level is no gain, and the start moves to it
        activity.ObserveLevels(11, 1);
        activity.ObserveLevels(12, 1);
        Assert.Equal("Reached level 12", activity.Entries[0].Text);

        activity.Clear();
        Assert.Empty(window.ActivityShown);
        activity.ObserveLevels(30, 0);
        Assert.Empty(activity.Entries);
    }

    //after a logout the attributes keep the old character's level; only a primary block of the new character is read
    [Fact]
    public void AfterAClearOnlyAPrimaryBlockSetsTheStart()
    {
        var profile = new SelfProfile();
        var attributes = new PlayerAttributes();
        var activity = new ActivityLog(attributes, profile);
        attributes.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 10 } });

        attributes.Clear();
        activity.Clear();

        //a packet with no primary block and a restore of the starts fire a change with the old level
        attributes.Update(new AttributesPacket { Experience = new ExperienceAttributes { Gold = 5 } });
        attributes.RestoreStarts(new SavedExperience(null, null));
        attributes.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 40 } });

        Assert.Empty(activity.Entries);
    }

    //after a character switch the window showed the character before's stats and could raise one with its points
    [Fact]
    public void AfterAClearThePacketsOfTheNextCharacterShowNoOldStatOrPoint()
    {
        var (window, _, attributes, activity) = NewWindow(unspent: 1);
        var raised = new List<Stat>();
        window.RaiseRequested += raised.Add;

        attributes.Clear();
        activity.Clear();
        attributes.Update(new AttributesPacket { Experience = new ExperienceAttributes { Gold = 5 } });

        Assert.Equal("0", window.StatShown(0));
        Assert.Equal("Points 0", window.RemainingShown);
        Assert.All(window.RaiseControls, raise => Assert.False(raise.Enabled));

        Click(window.RaiseControls[0]);
        Click(window.ConfirmButton);
        Assert.Empty(raised);
    }

    //a long line was squeezed until its glyphs overlapped; it ends in an ellipsis now, and its tooltip shows the full text
    [Fact]
    public void ALongActivityLineEndsInAnEllipsisAndKeepsItsFullText()
    {
        var (window, _, _, activity) = NewWindow();

        activity.ObserveLegendMarks([Mark("Born")]);
        activity.ObserveLegendMarks([Mark("Born"), Mark("Defeated the Great Dragon of the Eastern Woodlands (x3)")]);

        var shown = window.ActivityTextAt(0);
        var full = window.ActivityFullTextAt(0);

        Assert.EndsWith("…", shown);
        Assert.Equal("09:05 Legend: Defeated the Great Dragon of the Eastern Woodlands (x3)", full);
        Assert.StartsWith(shown[..^1], full);
        Assert.Null(window.ActivityFullTextAt(1));
    }

    [Theory]
    [InlineData("abc", 30, "abc")]
    [InlineData("abcdef", 30, "abcd…")]
    [InlineData("ab cdef", 24, "ab…")]
    [InlineData("abcdef", 3, "…")]
    [InlineData("ab\uD83D\uDE00cd", 24, "ab…")]
    [InlineData("ab\uD83D\uDE00cd", 30, "ab\uD83D\uDE00…")]
    public void FitWithEllipsisKeepsTheLongestStartThatFits(string text, int width, string expected)
        => Assert.Equal(expected, TextRenderer.FitWithEllipsis(text, width, value => value.Length * 6));

    [Fact]
    public void AnInvisibleMarkAndASecondMarkWithTheSameTextAreEntries()
    {
        var activity = new ActivityLog(new PlayerAttributes(), new SelfProfile());

        activity.ObserveLegendMarks([Mark("Fought")]);
        activity.ObserveLegendMarks([Mark("Fought"), Mark("Fought"), new LegendMarkEntry("Hidden", Color.Transparent)]);

        Assert.Equal(["Legend: Hidden", "Legend: Fought"], activity.Entries.Select(entry => entry.Text));
    }

    //the profile changes for its name and text too; only a new list of marks is read again
    [Fact]
    public void AProfileChangeWithTheSameMarksAddsNoEntry()
    {
        var profile = new SelfProfile();
        var activity = new ActivityLog(new PlayerAttributes(), profile);
        IReadOnlyList<LegendMarkEntry> marks = [Mark("Born")];

        profile.Update("Wizard", "", "", "", "", marks);
        profile.SetProfileText("Hi.");
        profile.Update("Wizard", "", "", "", "", [Mark("Born"), Mark("New")]);
        profile.SetName("A");

        Assert.Equal(["Legend: New"], activity.Entries.Select(entry => entry.Text));
    }

    [Fact]
    public void AGainOfTwoAbilityLevelsShowsTheSteps()
    {
        var activity = new ActivityLog(new PlayerAttributes(), new SelfProfile());

        activity.ObserveLevels(99, 1);
        activity.ObserveLevels(99, 3);

        Assert.Equal("Reached ability level 3 (+2)", activity.Entries[0].Text);
    }

    [Fact]
    public void TheActivityLogKeepsTheNewestEntries()
    {
        var activity = new ActivityLog(new PlayerAttributes(), new SelfProfile());
        activity.ObserveLevels(1, 0);

        for (byte level = 2; level < 2 + ActivityLog.MAX_ENTRIES + 5; level++)
            activity.ObserveLevels(level, 0);

        Assert.Equal(ActivityLog.MAX_ENTRIES, activity.Entries.Count);
        Assert.Equal($"Reached level {1 + ActivityLog.MAX_ENTRIES + 5}", activity.Entries[0].Text);
    }

    [Fact]
    public void TheSelfProfileClearsAndTrims()
    {
        var profile = new SelfProfile();
        var changes = 0;
        profile.Changed += () => changes++;

        profile.SetName("A");
        profile.SetName("A");
        profile.Update(" Rogue ", " G ", " R ", " T ", "N", [new LegendMarkEntry("m", Color.White)]);

        Assert.Equal(2, changes);
        changes = 0;
        Assert.Equal(("Rogue", "G", "R", "T"), (profile.ClassName, profile.GuildName, profile.GuildRank, profile.Title));
        Assert.True(profile.HasProfile);

        Assert.True(profile.SetPortrait([1, 2]));
        Assert.False(profile.SetPortrait([1, 2]));
        Assert.Equal("Human", profile.Species);
        Assert.Equal(["T"], profile.Titles);

        profile.Clear();

        Assert.False(profile.HasProfile);
        Assert.Empty(profile.LegendMarks);
        Assert.Equal(string.Empty, profile.Name);
    }

    private MainHudControl NewHud()
    {
        var proxy = DispatchProxy.Create<IWorldHud, MainHudControlTests.RecordingHud>();
        var hud = new MainHudControl(proxy, attributes: new PlayerAttributes(), chat: new Chat());
        Owned.Add(hud);

        return hud;
    }

    [Fact]
    public void GOpensAndClosesTheDetailsWindowWhichHasNoButton()
    {
        var hud = NewHud();
        bool Open() => hud.Windows.PanelFor(HudWindows.Details).Visible;

        Assert.DoesNotContain(HudWindows.Details, HudWindows.All);
        Assert.Null(hud.FindChild<UIElement>("details"));

        hud.HandleTabActivation(HudTab.Stats, false);
        Assert.True(Open());

        hud.HandleTabActivation(HudTab.Stats, false);
        Assert.False(Open());
    }

    [Fact]
    public void TheWindowsWithNoButtonArePanelWindowsOutOfTheRow()
    {
        Assert.All(HudWindows.Unlisted, window => Assert.NotNull(window.Panel));
        Assert.Empty(HudWindows.Unlisted.Intersect(HudWindows.All));
    }

    //the profile text box has the full width of the content of a compact panel, as the editor does
    [Fact]
    public void TheProfileTextBoxIsTheWidthOfACompactContent()
        => Assert.Equal(MainHudLayout.DETAILS_PROFILE_WIDTH, MainHudLayout.CompactLeft.Width - 2 * HudPanel.PADDING);

    [Fact]
    public void AWindowWithNoButtonTakesItsContent()
    {
        var hud = NewHud();
        var profile = new SelfProfile();
        var attributes = new PlayerAttributes();
        var content = new DetailsWindowControl(profile, attributes, new ActivityLog(attributes, profile));

        hud.Windows.BindPanel(HudWindows.Details, content);

        Assert.Same(content, hud.Windows.PanelFor(HudWindows.Details).Content);
    }
}

/// <summary>BRIG-151: the activity log of WorldState reads each attributes packet, and a reset forgets the session.</summary>
[Collection("WorldState")]
public class ActivityLogWorldStateTests : IDisposable
{
    public ActivityLogWorldStateTests() => WorldState.ResetAll();

    public void Dispose() => WorldState.ResetAll();

    [Fact]
    public void ALevelGainInTheAttributesGoesToTheLog()
    {
        WorldState.Attributes.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 10 } });
        WorldState.Attributes.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 11 } });

        Assert.Equal("Reached level 11", WorldState.Activity.Entries[0].Text);

        WorldState.SelfProfile.SetName("A");
        WorldState.ResetAll();

        Assert.Empty(WorldState.Activity.Entries);
        Assert.Equal(string.Empty, WorldState.SelfProfile.Name);

        //the reset also forgets the start, so the next character's first reading is no gain
        WorldState.Attributes.Update(new AttributesPacket { Primary = new PrimaryAttributes { Level = 40 } });
        Assert.Empty(WorldState.Activity.Entries);
    }
}
