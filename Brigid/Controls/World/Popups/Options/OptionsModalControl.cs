#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data;
using Brigid.Rendering;
using Brigid.Systems;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Popups.Options;

/// <summary>
///     The tabbed Options modal (Settings | Macros | Friends), built on <see cref="CenteredModalPanel" /> with a
///     <see cref="SelectableTab" /> column. Re-homes the three legacy prefab panels
///     (SettingsControl/MacrosListControl/FriendsListControl) onto one primitive modal.
///     <list type="bullet">
///         <item>Settings toggles auto-apply through <see cref="UserOptions" /> (server toggles route to the
///         server, client toggles persist) exactly as before; the font row cycles the UI face.</item>
///         <item>Macros and Friends edits <b>auto-commit</b> when their tab is left or the modal closes — the
///         legacy OK/Cancel (save/discard) split is replaced by lossless auto-save (both persist locally).</item>
///     </list>
///     Settings/Friends server/host coordination is exposed via <see cref="SettingsRequested" /> (request option
///     data on show) and <see cref="FriendsCommitted" /> (the host saves + mirrors the friend list).
/// </summary>
public sealed class OptionsModalControl : CenteredModalPanel
{
    public enum OptionsTab
    {
        Settings,
        Macros,
        Friends
    }

    private const int PANEL_W = 500;
    private const int PANEL_H = 320;

    private const int TAB_W = 84;
    private const int TAB_H = 26;
    private const int TAB_GAP = 4;
    private const int PANE_GAP = 10;

    private const int SETTING_ROW_H = 18;
    private const int SETTING_LABEL_X = 20;

    private const int MACRO_COUNT = 10;
    private const int MACRO_MAX_LENGTH = 63;
    private const int FRIEND_ROWS = 10;
    private const int FRIEND_MAX_LENGTH = 12;
    private const int ROW_STRIDE = 22;
    private const int BOX_HEIGHT = 20;

    private static readonly string[] TabTitles = ["Settings", "Macros", "Friends"];

    private readonly SelectableTab[] Tabs = new SelectableTab[3];
    private readonly UIPanel[] TabPanes = new UIPanel[3];

    private readonly CheckBox[] SettingChecks = new CheckBox[UserOptions.SETTING_COUNT];
    private readonly UILabel[] SettingLabels = new UILabel[UserOptions.SETTING_COUNT];
    private readonly string[] SettingBaseNames = new string[UserOptions.SETTING_COUNT];
    private TextButton FontButton = null!;

    private readonly UITextBox[] MacroBoxes = new UITextBox[MACRO_COUNT];

    private readonly UITextBox[] FriendCol1 = new UITextBox[FRIEND_ROWS];
    private readonly UITextBox[] FriendCol2 = new UITextBox[FRIEND_ROWS];
    private List<string> Friends = [];
    private int FriendsDataVersion;
    private int FriendsRenderedVersion = -1;

    private int PaneX;

    private OptionsTab Active = OptionsTab.Settings;

    /// <summary>Raised when the Settings tab is shown, so the host can request fresh option data from the server.</summary>
    public event Action? SettingsRequested;

    /// <summary>Raised when the Friends tab is committed (tab-left/close), so the host can save + mirror the list.</summary>
    public event Action? FriendsCommitted;

    private static UserOptions Options => WorldState.UserOptions;

    public OptionsModalControl()
        : base("Options", PANEL_W, PANEL_H)
    {
        var content = ContentBounds;
        PaneX = content.X + TAB_W + PANE_GAP;
        var paneRect = new Rectangle(PaneX, content.Y, content.Right - PaneX, content.Height);

        BuildTabColumn(content);
        BuildSettingsPane(paneRect);
        BuildMacrosPane(paneRect);
        BuildFriendsPane(paneRect);

        Options.SettingChanged += OnSettingChanged;

        SelectTabInternal(OptionsTab.Settings);
    }

    private void BuildTabColumn(Rectangle content)
    {
        for (var i = 0; i < Tabs.Length; i++)
        {
            var index = i;

            var tab = new SelectableTab(TabTitles[i], TAB_W, TAB_H)
            {
                X = content.X,
                Y = content.Y + i * (TAB_H + TAB_GAP)
            };

            tab.Clicked += () => SelectTab((OptionsTab)index);
            Tabs[i] = tab;
            AddChild(tab);
        }
    }

    private void BuildSettingsPane(Rectangle pane)
    {
        var content = new UIPanel { X = pane.X, Y = pane.Y, Width = pane.Width, Height = pane.Height };

        for (var i = 0; i < UserOptions.SETTING_COUNT; i++)
        {
            var index = i;
            var rowY = i * SETTING_ROW_H;

            var check = new CheckBox { X = 0, Y = rowY };
            check.CheckedChanged += _ => ToggleSetting(index);
            SettingChecks[i] = check;
            content.AddChild(check);

            var label = new UILabel
            {
                X = SETTING_LABEL_X,
                Y = rowY,
                Width = pane.Width - SETTING_LABEL_X,
                Height = CheckBox.DEFAULT_SIZE,
                PaddingLeft = 0,
                PaddingTop = 0,
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                ForegroundColor = TextColors.Default
            };

            SettingLabels[i] = label;
            content.AddChild(label);
        }

        FontButton = new TextButton($"Font : {FontEngine.Instance.ActiveFontName}", 220, 16)
        {
            X = 0,
            Y = UserOptions.SETTING_COUNT * SETTING_ROW_H + 4
        };
        FontButton.Clicked += CycleFont;
        content.AddChild(FontButton);

        //client-local default names (server settings are named by the server response)
        SetSettingName(6, "Use Group Window");
        SetSettingName(8, "Scroll Screen");
        SetSettingName(9, "the Shift key.");
        SetSettingName(10, "click character profile");
        SetSettingName(11, "NPC Record Mundane Chat");
        SetSettingName(12, "group recruiting");

        for (var i = 0; i < UserOptions.SETTING_COUNT; i++)
        {
            SettingChecks[i].Checked = Options[i];
            RefreshLabel(i);
        }

        TabPanes[0] = content;
        AddChild(content);
    }

    private void BuildMacrosPane(Rectangle pane)
    {
        var content = new UIPanel { X = pane.X, Y = pane.Y, Width = pane.Width, Height = pane.Height };

        for (var i = 0; i < MACRO_COUNT; i++)
        {
            MacroBoxes[i] = new UITextBox
            {
                Name = $"Macro{i}",
                X = 0,
                Y = i * ROW_STRIDE,
                Width = pane.Width,
                Height = BOX_HEIGHT,
                MaxLength = MACRO_MAX_LENGTH,
                ForegroundColor = LegendColors.White,
                IsTabStop = true
            };

            content.AddChild(MacroBoxes[i]);
        }

        TabPanes[1] = content;
        AddChild(content);
    }

    private void BuildFriendsPane(Rectangle pane)
    {
        var content = new UIPanel { X = pane.X, Y = pane.Y, Width = pane.Width, Height = pane.Height };
        var colWidth = (pane.Width - PANE_GAP) / 2;

        for (var i = 0; i < FRIEND_ROWS; i++)
        {
            FriendCol1[i] = new UITextBox
            {
                Name = $"Left{i}",
                X = 0,
                Y = i * ROW_STRIDE,
                Width = colWidth,
                Height = BOX_HEIGHT,
                MaxLength = FRIEND_MAX_LENGTH,
                ForegroundColor = LegendColors.White,
                IsTabStop = true
            };

            FriendCol2[i] = new UITextBox
            {
                Name = $"Right{i}",
                X = colWidth + PANE_GAP,
                Y = i * ROW_STRIDE,
                Width = colWidth,
                Height = BOX_HEIGHT,
                MaxLength = FRIEND_MAX_LENGTH,
                ForegroundColor = LegendColors.White,
                IsTabStop = true
            };

            content.AddChild(FriendCol1[i]);
            content.AddChild(FriendCol2[i]);
        }

        TabPanes[2] = content;
        AddChild(content);
    }

    // ── tab selection ──

    public void SelectTab(OptionsTab tab)
    {
        if (tab == Active)
            return;

        CommitTab(Active);
        SelectTabInternal(tab);
    }

    private void SelectTabInternal(OptionsTab tab)
    {
        Active = tab;

        for (var i = 0; i < Tabs.Length; i++)
        {
            Tabs[i].IsSelected = i == (int)tab;
            TabPanes[i].Visible = i == (int)tab;
        }

        if (tab == OptionsTab.Settings)
            SettingsRequested?.Invoke();
        else if (tab == OptionsTab.Friends)
            //populate eagerly (not just in Draw) so a commit that happens before the first Draw — e.g.
            //F10 then Escape in the same input frame — reads real rows, not empty boxes. Version-gated, so
            //re-selecting Friends never clobbers in-progress edits.
            RefreshFriendsCache();
    }

    private void CommitTab(OptionsTab tab)
    {
        switch (tab)
        {
            case OptionsTab.Macros:
                DataContext.LocalPlayerSettings.SaveMacros(GetMacroValues());

                break;

            case OptionsTab.Friends:
                FriendsCommitted?.Invoke();

                break;
        }
    }

    // ── show / hide ──

    /// <summary>Opens the modal to a specific tab (or switches to it if already open, committing the previous tab).</summary>
    public void Show(OptionsTab tab)
    {
        if (Visible)
        {
            SelectTab(tab);

            return;
        }

        SelectTabInternal(tab);
        base.Show();
    }

    public override void Show() => Show(OptionsTab.Settings);

    public override void Hide()
    {
        if (!Visible)
            return;

        CommitTab(Active);
        base.Hide();
    }

    // ── settings binding ──

    private void ToggleSetting(int index)
    {
        Options.Toggle(index);

        //re-sync from the authoritative value: client toggles stick, server toggles revert until the server
        //confirms (Toggle leaves server settings unchanged, so this undoes the checkbox's optimistic flip).
        SettingChecks[index].Checked = Options[index];
    }

    private void OnSettingChanged(int index, bool value)
    {
        if (index is < 0 or >= UserOptions.SETTING_COUNT)
            return;

        SettingChecks[index].Checked = Options[index];
        RefreshLabel(index);
    }

    private void RefreshLabel(int index)
    {
        var baseName = SettingBaseNames[index];

        if (string.IsNullOrEmpty(baseName))
            return;

        var value = Options[index];

        var text = index switch
        {
            8 => $"Scroll Screen : {(value ? "Smooth" : "Rough")}",
            9 => value ? "the Shift key." : "not use the Shift key.",
            _ when UserOptions.IsServerSetting(index) => baseName,
            _ => $"{baseName} :{(value ? "ON" : "OFF")}"
        };

        SettingLabels[index].ForegroundColor = TextColors.Default;
        SettingLabels[index].Text = text;
    }

    /// <summary>Sets the display name for a setting (server response for server settings). Refreshes its label.</summary>
    public void SetSettingName(int index, string name)
    {
        if (index is < 0 or >= UserOptions.SETTING_COUNT)
            return;

        SettingBaseNames[index] = name;
        RefreshLabel(index);
    }

    private void CycleFont()
    {
        FontEngine.Instance.CycleFont();
        ClientSettings.FontIndex = FontEngine.Instance.ActiveFontIndex;
        ClientSettings.Save();
        FontButton.SetText($"Font : {FontEngine.Instance.ActiveFontName}");
    }

    // ── macros ──

    public string GetMacroValue(int index) => index is >= 0 and < MACRO_COUNT ? MacroBoxes[index].Text : string.Empty;

    public string[] GetMacroValues()
    {
        var values = new string[MACRO_COUNT];

        for (var i = 0; i < MACRO_COUNT; i++)
            values[i] = MacroBoxes[i].Text;

        return values;
    }

    public void SetMacros(string[] macros)
    {
        for (var i = 0; i < MACRO_COUNT; i++)
            MacroBoxes[i].Text = i < macros.Length ? macros[i] : string.Empty;
    }

    // ── friends ──

    /// <summary>Populates the friends list. Slots fill column 1 first (rows 0-9), then column 2 (rows 10-19).</summary>
    public void SetFriends(List<string> friends)
    {
        Friends = friends;
        FriendsDataVersion++;
    }

    /// <summary>Non-empty friend names in slot order — column 1 top-to-bottom, then column 2.</summary>
    public List<string> GetFriendNames()
    {
        var names = new List<string>(FRIEND_ROWS * 2);

        foreach (var box in FriendCol1)
            if (!string.IsNullOrWhiteSpace(box.Text))
                names.Add(box.Text.Trim());

        foreach (var box in FriendCol2)
            if (!string.IsNullOrWhiteSpace(box.Text))
                names.Add(box.Text.Trim());

        return names;
    }

    private void RefreshFriendsCache()
    {
        if (FriendsRenderedVersion == FriendsDataVersion)
            return;

        FriendsRenderedVersion = FriendsDataVersion;

        for (var i = 0; i < FRIEND_ROWS; i++)
        {
            FriendCol1[i].Text = i < Friends.Count ? Friends[i] : string.Empty;

            var rightIndex = i + FRIEND_ROWS;
            FriendCol2[i].Text = rightIndex < Friends.Count ? Friends[rightIndex] : string.Empty;
        }
    }

    public override void Dispose()
    {
        //Options is the static WorldState.UserOptions singleton, which outlives this modal — detach so a
        //re-created WorldScreen's modal doesn't accumulate handlers on it.
        Options.SettingChanged -= OnSettingChanged;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (Visible)
            RefreshFriendsCache();

        base.Draw(spriteBatch);

        if (!Visible)
            return;

        //vertical divider between the tab column and the content pane.
        DrawRectClipped(spriteBatch, new Rectangle(ScreenX + PaneX - PANE_GAP / 2, ScreenY + ContentTop, 1, ContentBounds.Height), DialogPalette.Divider);
    }
}
