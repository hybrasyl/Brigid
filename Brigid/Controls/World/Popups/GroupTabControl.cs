#region
using Brigid.Controls.Components;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     Tabbed group window using _ngcmain prefab. Tab 0 = group member list, Tab 1 = group recruitment config. Hosts
///     <see cref="GroupTab" /> and <see cref="GroupRecruitPanel" /> as child panels swapped by tab selection.
/// </summary>
public sealed class GroupTabControl : PrefabPanel
{
    public GroupTab MembersPanel { get; }
    public GroupRecruitPanel RecruitPanel { get; }

    private UIButton? Tab0Button { get; }
    private UIButton? Tab1Button { get; }

    public GroupTabControl()
        : base("_ngcmain", false)
    {
        Name = "GroupMain";
        Visible = false;
        UsesControlStack = true;
        X = 0;
        Y = 0;

        var cache = UiRenderer.Instance!;

        //tab buttons — prefab image is _ngcbtnb.spf (selected/big), swap to selectedtexture
        //and load _ngcbtns.spf (normal/small) from tab0_s/tab1_s prefab entries as normaltexture
        Tab0Button = CreateButton("TAB0");

        if (Tab0Button is not null)
        {
            Tab0Button.CenterTexture = true;
            Tab0Button.SelectedTexture = Tab0Button.NormalTexture;
            Tab0Button.NormalTexture = cache.GetPrefabTexture("_ngcmain", "TAB0_S", 0);
            Tab0Button.Clicked += () => SwitchTab(0);
        }

        Tab1Button = CreateButton("TAB1");

        if (Tab1Button is not null)
        {
            Tab1Button.CenterTexture = true;
            Tab1Button.SelectedTexture = Tab1Button.NormalTexture;
            Tab1Button.NormalTexture = cache.GetPrefabTexture("_ngcmain", "TAB1_S", 0);
            Tab1Button.Clicked += () =>
            {
                SwitchTab(1);
                OnRecruitTabOpened?.Invoke();
            };
        }

        //create child panels — positioned at dlgframe rect (0,0 within the main container).
        //UsesControlStack is forced false for these nested instances: they're always wrapped
        //by GroupTabControl and must not push independently. If they did, SwitchTab(0)'s
        //MembersPanel.Show() call would push MembersPanel onto the InputDispatcher control
        //stack at construction time, which permanently trips the WorldScreen stack guard
        //in OnRootKeyDown and disables game hotkeys (Y, T, J, B, F1-F10, A/S/D/F/G/H, etc.)
        //until the user opens-and-closes the group window once. Only the top-level
        //GroupTabControl participates in the control stack.
        MembersPanel = new GroupTab();
        MembersPanel.Visible = true;
        MembersPanel.UsesControlStack = false;

        RecruitPanel = new GroupRecruitPanel();
        RecruitPanel.Visible = false;
        RecruitPanel.UsesControlStack = false;

        AddChild(MembersPanel);
        AddChild(RecruitPanel);

        //wire close events from child panels
        MembersPanel.OnClose += () =>
        {
            Hide();
            OnClose?.Invoke();
        };

        RecruitPanel.OnClose += () =>
        {
            Hide();
            OnClose?.Invoke();
        };

        SwitchTab(0);
    }

    public override void Dispose()
    {
        MembersPanel.Dispose();
        RecruitPanel.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);
    }

    public event CloseHandler? OnClose;

    /// <summary>
    ///     Fired when the user clicks the recruit tab button. Subscribers decide whether to send
    ///     a self-ViewGroupBox query (when HasActiveGroupBox is true) so the panel populates from
    ///     the authoritative server state instead of showing a stale OwnerNew blank form.
    /// </summary>
    public event Action? OnRecruitTabOpened;

    /// <summary>
    ///     Shows the window on the members tab.
    /// </summary>
    public void ShowMembers()
    {
        //prime the recruit panel to OwnerNew defaults once per panel-open. If the
        //user later switches to TAB1 with HasActiveGroupBox==true, the
        //OnRecruitTabOpened handler sends ViewGroupBox(self) and the server's
        //ShowGroupBox(self) response overrides to OwnerEdit via ShowRecruitOwnerEdit.
        //Priming here (not on every TAB1 click) preserves in-progress typing when
        //the user toggles between tabs within a single panel session.
        RecruitPanel.ShowAsOwner();
        SwitchTab(0);
        Show();
    }

    /// <summary>
    ///     Shows the window on the recruit tab in owner-edit mode, populated from server data.
    /// </summary>
    public void ShowRecruitOwnerEdit(DisplayGroupBoxInfo info)
    {
        SwitchTab(1);
        RecruitPanel.ShowAsOwnerEdit(info);
        Show();
    }

    /// <summary>
    ///     Shows the window on the recruit tab in viewer mode, populated from server data.
    /// </summary>
    public void ShowRecruitViewer(string sourceName, DisplayGroupBoxInfo info)
    {
        SwitchTab(1);
        RecruitPanel.ShowAsViewer(sourceName, info);
        Show();
    }

    private void SwitchTab(int tab)
    {
        if (tab == 0)
        {
            //reset interaction state on the outgoing panel to clear any focused textbox
            RecruitPanel.ResetInteractionState();
            RecruitPanel.Hide();
            MembersPanel.Show();
        } else
        {
            MembersPanel.ResetInteractionState();
            MembersPanel.Hide();
            RecruitPanel.Show();
        }

        if (Tab0Button is not null)
        {
            Tab0Button.IsSelected = tab == 0;
            Tab0Button.ZIndex = tab == 0 ? 1 : -1;
        }

        if (Tab1Button is not null)
        {
            Tab1Button.IsSelected = tab == 1;
            Tab1Button.ZIndex = tab == 1 ? 1 : -1;
        }
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            OnClose?.Invoke();
            e.Handled = true;
        }
    }

    public override void Hide()
    {
        //ensure both children are also removed from the control stack so they don't
        //orphan above a hidden parent — SwitchTab only pushes one child at a time,
        //but closing the window should clear both regardless of which tab was active.
        MembersPanel.Hide();
        RecruitPanel.Hide();

        base.Hide();
    }
}