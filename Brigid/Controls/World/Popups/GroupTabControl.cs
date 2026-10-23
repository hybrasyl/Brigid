#region
using Brigid.Controls.Generic;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The group window (BRIG-126 step 3c), a themed <see cref="CenteredModalPanel" /> in place of the legacy
///     <c>_ngcmain</c> prefab. Two tabs: Members (<see cref="GroupTab" />) and Recruit (<see cref="GroupRecruitPanel" />).
///     The bottom bar shows Close, and on the Recruit tab the actions of the form's mode: Begin, or Modify and Reset.
///     Close only closes the window; it does not remove your group box. Another player's group box shows in
///     <see cref="GroupBoxViewerControl" />.
///     <para>
///         The window sits at the top left of the HUD viewport (D11). It is not modal, and the keys that it does not use go
///         on to the screen (D10).
///     </para>
/// </summary>
public sealed class GroupTabControl : CenteredModalPanel
{
    private const int TAB_W = 72;
    private const int TAB_H = 18;
    private const int PANE_GAP = 4;
    private const int ACTION_W = 56;
    private const int PANE_W = GroupRecruitPanel.FORM_W;
    private const int PANE_H = GroupTab.HEIGHT > GroupRecruitPanel.FORM_H ? GroupTab.HEIGHT : GroupRecruitPanel.FORM_H;
    private const int CONTENT_H = TAB_H + PANE_GAP + PANE_H;

    private const int MEMBERS_TAB = 0;
    private const int RECRUIT_TAB = 1;

    private readonly TabStrip Tabs;
    private readonly TextButton ResetButton;
    private readonly TextButton ModifyButton;
    private readonly TextButton BeginButton;

    public GroupTab MembersPanel { get; }
    public GroupRecruitPanel RecruitPanel { get; }

    /// <summary>
    ///     Raised when the player opens the Recruit tab. The screen asks the server for your group box when you have one, so
    ///     that the form shows the server's values in place of a blank form.
    /// </summary>
    public event Action? OnRecruitTabOpened;

    public GroupTabControl()
        : base("Group", PANE_W + 2 * PADDING, TITLE_H + 1 + CONTENT_H + BOTTOM_H, useTheme: true)
    {
        Name = "group";

        var content = ContentBounds;
        var paneY = content.Y + TAB_H + PANE_GAP;

        Tabs = new TabStrip(["Members", "Recruit"], TAB_W, TAB_H)
        {
            Name = "tabs",
            X = content.X,
            Y = content.Y
        };
        Tabs.TabClicked += OnTabClicked;
        AddChild(Tabs);

        MembersPanel = new GroupTab(PANE_W)
        {
            Name = "members",
            X = content.X,
            Y = paneY
        };
        AddChild(MembersPanel);

        RecruitPanel = new GroupRecruitPanel
        {
            Name = "recruit",
            X = content.X,
            Y = paneY
        };
        RecruitPanel.OnClose += Hide;
        RecruitPanel.ModeChanged += ShowActions;
        AddChild(RecruitPanel);

        //packed right to left after Close, so that the bar reads Modify, Reset, Close
        ResetButton = AddBottomBarButton("Reset", ACTION_W, RecruitPanel.RemoveBox, "reset");
        ModifyButton = AddBottomBarButton("Modify", ACTION_W, RecruitPanel.ModifyBox, "modify");
        BeginButton = AddBottomBarButton("Begin", ACTION_W, RecruitPanel.BeginBox, "begin");

        SwitchTab(MEMBERS_TAB);
    }

    //the window is used beside the world: Enter for chat and the other keys above the screen's stack guard still work
    protected override bool LetsKeysThrough => true;

    //the top left of the HUD viewport, as the legacy window (D11)
    protected override Point PositionIn(Rectangle viewport) => viewport.Location;

    /// <summary>The index of the tab that shows: 0 for Members, 1 for Recruit. Internal for the tests.</summary>
    internal int SelectedTab => Tabs.SelectedIndex;

    /// <summary>Clicks a tab, as the mouse does. Internal for the tests.</summary>
    internal void ClickTab(int index) => OnTabClicked(index);

    /// <summary>Shows the window on the Members tab, with a new Recruit form.</summary>
    public void ShowMembers()
    {
        //each open resets the form; a change of tab keeps the text. When you have a group box, the Recruit tab asks the
        //server for it, and ShowRecruitOwnerEdit fills the form
        RecruitPanel.ShowAsOwner();
        SwitchTab(MEMBERS_TAB);
        Show();
    }

    /// <summary>Shows the window on the Recruit tab, to change your group box with the values from the server.</summary>
    public void ShowRecruitOwnerEdit(GroupRecruitInfo info)
    {
        RecruitPanel.ShowAsOwnerEdit(info);
        SwitchTab(RECRUIT_TAB);
        Show();
    }

    private void OnTabClicked(int index)
    {
        if (index == Tabs.SelectedIndex)
            return;

        SwitchTab(index);

        if (index == RECRUIT_TAB)
            OnRecruitTabOpened?.Invoke();
    }

    //a hidden tab drops its focus and its pressed state
    private void SwitchTab(int index)
    {
        Tabs.SetSelected(index);
        MembersPanel.Visible = index == MEMBERS_TAB;
        RecruitPanel.Visible = index == RECRUIT_TAB;
        ShowActions();
    }

    private void ShowActions()
    {
        if (Tabs.SelectedIndex != RECRUIT_TAB)
        {
            SetBottomBarActions();

            return;
        }

        //the window shows only your own group box; the viewer mode is for GroupBoxViewerControl
        if (RecruitPanel.Mode == GroupRecruitMode.OwnerEdit)
            SetBottomBarActions(ModifyButton, ResetButton);
        else
            SetBottomBarActions(BeginButton);
    }
}
