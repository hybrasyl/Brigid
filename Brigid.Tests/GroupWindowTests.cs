#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.World.Popups;
using Brigid.Definitions;
using Brigid.Rendering;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     BRIG-126 step 3c: the group window is a themed panel at the top left of the viewport, with a Members tab and a
///     Recruit tab. The bottom bar shows the actions of the recruit form's mode. The group box viewer shows the same form,
///     read-only, with Join. Both panels let the keys that they do not use go on to the screen.
/// </summary>
[Collection("FontEngine")]
public sealed class GroupWindowTests : IDisposable
{
    private readonly GroupTabControl Window;
    private readonly GroupBoxViewerControl Viewer;
    private readonly string PreviousPlayerName = WorldState.PlayerName;

    public GroupWindowTests()
    {
        FontEngine.Initialize(0);
        WorldState.Group.ResetAll();
        WorldState.PlayerName = "Angelique";
        Window = new GroupTabControl();
        Viewer = new GroupBoxViewerControl();
    }

    public void Dispose()
    {
        WorldState.Group.ResetAll();
        WorldState.PlayerName = PreviousPlayerName;
        Window.Dispose();
        Viewer.Dispose();
    }

    private static GroupRecruitInfo Info()
        => new()
        {
            RecruiterName = "Sabrael",
            GroupName = "Mileth crypt",
            Note = "Bring a priest.",
            StartingLevel = 10,
            EndingLevel = 40,
            WarriorsWanted = 2,
            CurrentWarriors = 1,
            WizardsWanted = 1,
            CurrentWizards = 0,
            RoguesWanted = 1,
            CurrentRogues = 1,
            PriestsWanted = 2,
            CurrentPriests = 0,
            MonksWanted = 0,
            CurrentMonks = 0
        };

    private TextButton Action(string name) => Window.FindChild<TextButton>(name)!;

    //the actions on the bottom bar that show now, without Close
    private string[] ShownActions()
        => new[] { "begin", "modify", "reset" }
           .Where(name => Action(name).Visible)
           .ToArray();

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        Assert.Equal("group", Window.Name);
        UiAssert.NamedTree(Window);

        Assert.Equal("groupbox", Viewer.Name);
        UiAssert.NamedTree(Viewer);
    }

    [Fact]
    public void TheWindowSitsAtTheTopLeftOfTheViewport_AndTheViewerInItsCentre()
    {
        var viewport = new Rectangle(10, 20, 600, 340);

        Window.SetViewportBounds(viewport);
        Viewer.SetViewportBounds(viewport);
        Window.ShowMembers();

        Assert.Equal(new Point(10, 20), new Point(Window.X, Window.Y));
        Assert.Equal(10 + (600 - Viewer.Width) / 2, Viewer.X);
        Assert.Equal(20 + (340 - Viewer.Height) / 2, Viewer.Y);

        //a change of HUD layout moves the open window
        Window.SetViewportBounds(new Rectangle(0, 0, 640, 424));

        Assert.Equal(Point.Zero, new Point(Window.X, Window.Y));
    }

    [Fact]
    public void TheMembersTabShowsEachMember_MarksTheLeader_AndGivesTheLeaderKick()
    {
        WorldState.Group.ParseAndSet("Group members\n* Angelique\n  Sabrael\n  Kedian\nTotal 3");
        var kicked = new List<string>();
        Window.MembersPanel.OnKick += kicked.Add;

        Window.ShowMembers();
        Window.Update(new GameTime());

        Assert.Equal(("Angelique", false, true), Window.MembersPanel.RowState(0));
        Assert.Equal(("Sabrael", true, false), Window.MembersPanel.RowState(1));
        Assert.Equal(("Kedian", true, false), Window.MembersPanel.RowState(2));
        Assert.Equal((string.Empty, false, false), Window.MembersPanel.RowState(3));
        Assert.False(Window.MembersPanel.ShowsEmpty);

        Window.MembersPanel.KickButton(1).PerformClick();

        Assert.Equal(["Sabrael"], kicked);
    }

    [Fact]
    public void KickRemovesTheNameTheRowShows_EvenWhenTheGroupChangedSince()
    {
        WorldState.Group.ParseAndSet("Group members\n* Angelique\n  Sabrael\n  Kedian\nTotal 3");
        var kicked = new List<string>();
        Window.MembersPanel.OnKick += kicked.Add;

        Window.ShowMembers();
        Window.Update(new GameTime());

        //Sabrael leaves; the rows refresh only in the next update
        WorldState.Group.ParseAndSet("Group members\n* Angelique\n  Kedian\nTotal 2");
        Window.MembersPanel.KickButton(1).PerformClick();

        Assert.Equal(["Sabrael"], kicked);
    }

    [Fact]
    public void AMemberWhoIsNotTheLeaderHasNoKick()
    {
        WorldState.Group.ParseAndSet("Group members\n* Sabrael\n  Angelique\nTotal 2");

        Window.ShowMembers();
        Window.Update(new GameTime());

        Assert.Equal(("Sabrael", false, true), Window.MembersPanel.RowState(0));
        Assert.Equal(("Angelique", false, false), Window.MembersPanel.RowState(1));
    }

    [Fact]
    public void WithNoGroup_TheMembersTabSaysSo()
    {
        Window.ShowMembers();
        Window.Update(new GameTime());

        Assert.True(Window.MembersPanel.ShowsEmpty);
        Assert.Equal((string.Empty, false, false), Window.MembersPanel.RowState(0));
    }

    [Fact]
    public void TheRecruitTabAsksForYourBoxOnce_AndShowsBegin()
    {
        var opened = 0;
        Window.OnRecruitTabOpened += () => opened++;
        Window.ShowMembers();

        Assert.Equal(0, Window.SelectedTab);
        Assert.Empty(ShownActions());
        Assert.True(Window.MembersPanel.Visible);

        Window.ClickTab(1);
        Window.ClickTab(1);

        Assert.Equal(1, opened);
        Assert.Equal(1, Window.SelectedTab);
        Assert.False(Window.MembersPanel.Visible);
        Assert.True(Window.RecruitPanel.Visible);
        Assert.Equal(["begin"], ShownActions());
    }

    [Fact]
    public void AChangeOfTabKeepsWhatThePlayerTyped_AndANewOpenClearsIt()
    {
        Window.ShowMembers();
        Window.ClickTab(1);
        Window.RecruitPanel.TitleField.Text = "Crypt run";

        Window.ClickTab(0);
        Window.ClickTab(1);

        Assert.Equal("Crypt run", Window.RecruitPanel.TitleField.Text);

        Window.Hide();
        Window.ShowMembers();

        Assert.Equal(string.Empty, Window.RecruitPanel.TitleField.Text);
    }

    [Fact]
    public void BeginSendsTheFormAndCloses()
    {
        var sent = new List<object>();
        var closes = 0;

        Window.RecruitPanel.OnCreateGroupBox += (name, note, minLevel, maxLevel, warriors, wizards, rogues, priests, monks)
            => sent.AddRange([name, note, minLevel, maxLevel, warriors, wizards, rogues, priests, monks]);
        Window.OnClose += () => closes++;

        Window.ShowMembers();
        Window.ClickTab(1);
        Window.RecruitPanel.TitleField.Text = "Crypt run";
        Window.RecruitPanel.ExtraField.Text = "Bring a priest.";
        Window.RecruitPanel.MinLevelField.Text = "10";
        Window.RecruitPanel.MaxLevelField.Text = string.Empty;
        Window.RecruitPanel.WantedCountFields[3].Text = "2";

        Action("begin").PerformClick();

        Assert.Equal(["Crypt run", "Bring a priest.", (byte)10, (byte)99, (byte)0, (byte)0, (byte)0, (byte)2, (byte)0], sent);
        Assert.False(Window.Visible);
        Assert.Equal(1, closes);
    }

    //HTOO-64 and BRIG-6: the editor and the viewer use the same fields, so a row-order fault cancels on a round trip.
    //These two tests pin the order of the rows against the class names, one direction each
    [Fact]
    public void EachClassRowSendsTheCapOfItsOwnClass()
    {
        byte[]? caps = null;
        Window.RecruitPanel.OnCreateGroupBox += (_, _, _, _, warriors, wizards, rogues, priests, monks)
            => caps = [warriors, wizards, rogues, priests, monks];

        Window.ShowMembers();
        Window.ClickTab(1);
        string[] classes = ["Warrior", "Wizard", "Rogue", "Priest", "Monk"];

        for (var i = 0; i < classes.Length; i++)
        {
            Assert.Equal(classes[i], Window.RecruitPanel.FindChild<UILabel>($"{classes[i].ToLowerInvariant()}_caption")!.Text);
            Window.RecruitPanel.WantedCountFields[i].Text = (i + 1).ToString();
        }

        Action("begin").PerformClick();

        Assert.Equal([1, 2, 3, 4, 5], caps);
    }

    [Fact]
    public void EachClassRowShowsTheCountsOfItsOwnClass()
    {
        var info = new GroupRecruitInfo
        {
            RecruiterName = "Sabrael",
            GroupName = "Order",
            Note = string.Empty,
            StartingLevel = 1,
            EndingLevel = 99,
            WarriorsWanted = 1,
            WizardsWanted = 2,
            RoguesWanted = 3,
            PriestsWanted = 4,
            MonksWanted = 5,
            CurrentWarriors = 6,
            CurrentWizards = 7,
            CurrentRogues = 8,
            CurrentPriests = 9,
            CurrentMonks = 10
        };

        Viewer.ShowAsViewer("Sabrael", info);

        Assert.Equal(["1", "2", "3", "4", "5"], Viewer.Form.WantedCountFields.Select(field => field.Text));
        Assert.Equal(["6", "7", "8", "9", "10"], Viewer.Form.CurrentCounts.Select(count => count.Current));
    }

    [Fact]
    public void YourBoxShowsModifyAndReset_ModifyStaysOpen_ResetRemovesAndCloses()
    {
        var creates = 0;
        var removes = 0;
        Window.RecruitPanel.OnCreateGroupBox += (_, _, _, _, _, _, _, _, _) => creates++;
        Window.RecruitPanel.OnRemoveGroupBox += () => removes++;

        Window.ShowRecruitOwnerEdit(Info());

        Assert.Equal(1, Window.SelectedTab);
        Assert.Equal(["modify", "reset"], ShownActions());
        Assert.All(Window.RecruitPanel.CurrentCounts, count => Assert.False(count.Visible));
        Assert.Equal("Mileth crypt", Window.RecruitPanel.TitleField.Text);
        Assert.Equal("2", Window.RecruitPanel.WantedCountFields[0].Text);
        Assert.True(Action("modify").X < Action("reset").X);

        Action("modify").PerformClick();

        Assert.Equal(1, creates);
        Assert.True(Window.Visible);

        Action("reset").PerformClick();

        Assert.Equal(1, removes);
        Assert.False(Window.Visible);
    }

    [Fact]
    public void TheLevelAndCountFieldsTakeTwoDigits()
    {
        var form = Window.RecruitPanel;

        UITextBox[] numberFields = [form.MinLevelField, form.MaxLevelField, .. form.WantedCountFields];

        Assert.All(
            numberFields,
            field =>
            {
                Assert.True(field.DigitsOnly);
                Assert.Equal(2, field.MaxLength);
            });

        Assert.False(form.TitleField.DigitsOnly);
        Assert.Equal(18, form.TitleField.MaxLength);
        Assert.Equal(60, form.ExtraField.MaxLength);
        Assert.True(form.ExtraField.IsMultiLine);
    }

    [Fact]
    public void TheViewerIsReadOnly_ShowsTheCountsNow_AndJoinAsksToJoin()
    {
        var joins = new List<string>();
        Viewer.OnRequestJoin += joins.Add;

        Viewer.ShowAsViewer("Sabrael", Info());

        Assert.True(Viewer.Visible);
        Assert.Equal(GroupRecruitMode.Viewer, Viewer.Form.Mode);
        //disabled, so that a click puts no focus in a field and the keys go on to the screen
        Assert.False(Viewer.Form.TitleField.Enabled);
        Assert.False(Viewer.Form.ExtraField.Enabled);
        Assert.False(Viewer.Form.WantedCountFields[0].Enabled);
        Assert.Equal(["1", "0", "1", "0", "0"], Viewer.Form.CurrentCounts.Select(count => count.Current));
        Assert.All(Viewer.Form.CurrentCounts, count => Assert.True(count.Visible));

        Viewer.FindChild<TextButton>("join")!.PerformClick();

        Assert.Equal(["Sabrael"], joins);
        Assert.False(Viewer.Visible);
    }

    [Fact]
    public void YourBoxFromTheServerOpensTheRecruitTab_OverAnOpenMembersTab()
    {
        Window.ShowMembers();

        Window.ShowRecruitOwnerEdit(Info());

        Assert.True(Window.Visible);
        Assert.Equal(1, Window.SelectedTab);
        Assert.True(Window.RecruitPanel.Visible);
        Assert.Equal(["modify", "reset"], ShownActions());
    }

    [Fact]
    public void AnOwnerFormAfterTheViewerIsEditableAgain()
    {
        using var form = new GroupRecruitPanel();
        var modes = 0;
        form.ModeChanged += () => modes++;

        form.ShowAsViewer("Sabrael", Info());

        Assert.False(form.TitleField.Enabled);

        form.ShowAsOwner();

        Assert.Equal(2, modes);
        Assert.Equal(GroupRecruitMode.OwnerNew, form.Mode);
        Assert.True(form.TitleField.Enabled);
        Assert.Equal(string.Empty, form.TitleField.Text);
        Assert.All(form.CurrentCounts, count => Assert.False(count.Visible));
    }

    [Fact]
    public void EscapeCloses_AndAKeyThePanelDoesNotUseGoesOnToTheScreen()
    {
        var closes = 0;
        Window.OnClose += () => closes++;
        Window.ShowMembers();

        var enter = new KeyDownEvent { Key = Keys.Enter };
        Window.OnKeyDown(enter);

        Assert.False(enter.Handled);
        Assert.True(Window.Visible);

        var escape = new KeyDownEvent { Key = Keys.Escape };
        Window.OnKeyDown(escape);

        Assert.True(escape.Handled);
        Assert.False(Window.Visible);
        Assert.Equal(1, closes);

        Viewer.ShowAsViewer("Sabrael", Info());
        var viewerEnter = new KeyDownEvent { Key = Keys.Enter };
        Viewer.OnKeyDown(viewerEnter);

        Assert.False(viewerEnter.Handled);
    }
}
