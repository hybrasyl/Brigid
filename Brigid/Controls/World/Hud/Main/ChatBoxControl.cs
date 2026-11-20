#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The chat box of the new HUD (BRIG-145), over the bottom left of the world (BRIG-127 D7): a row of tabs, the
///     scrollback of the selected tab and the chat input. Each tab is a filtered view of the one chat log (D13, D24), so
///     the separate message history and the orange bar go into its tabs (D10). A tab shows a mark when an entry for it
///     arrives while the player cannot see it: another tab is selected, or the box is hidden.
///     <para>
///         The lines go down to the bottom of the box, the newest at the bottom; while the chat input has the focus, it
///         shows under them. Clicks do not focus the input; only the chat keys do, as on the classic HUD. The hide button
///         collapses the box to its tab row, at the bottom of the box; the chat input then opens over the row.
///     </para>
///     <para>
///         The player adds up to <see cref="MAX_CUSTOM_TABS" /> custom tabs with the <c>+</c> button after the tabs, and
///         edits or removes one with a right click on it (D24, D26). The box raises the requests; the world screen opens
///         the editor and the menu, and calls back to change the tabs. Each name is unique in the row.
///     </para>
///     <para>
///         The box is translucent: the <see cref="ThemeRoles.HudChatBox" /> part, else the flat frame fill with alpha.
///         While it shows, it takes the mouse, so a click on the chat does not walk; collapsed, only its tab row takes
///         it.
///     </para>
/// </summary>
public sealed class ChatBoxControl : UIPanel
{
    /// <summary>The most custom tabs that the box holds.</summary>
    public const int MAX_CUSTOM_TABS = 3;

    //the alpha of the flat fill: the world shows through the box
    private const byte FLAT_ALPHA = 150;

    private readonly IconButton AddButton;
    private readonly UIPanel Backdrop;
    private readonly IconButton HideButton;
    private readonly Chat Log;
    private readonly FontGenerationStamp TabLayout = new();
    private readonly List<(ChatTab Tab, SelectableTab Control)> Tabs = [];
    private int NextCustomId;

    public ChatBoxControl(Chat log)
    {
        Name = "chat_box";
        Log = log;
        SetBounds(MainHudLayout.ChatBox);
        BackgroundColor = DialogPalette.FrameFill * (FLAT_ALPHA / 255f);
        BorderColor = DialogPalette.FrameBorder;

        //the tab row takes the mouse between its tabs, also while the box passes the mouse through
        Backdrop = new UIPanel { Name = "tab_row" };
        AddChild(Backdrop);

        foreach (var tab in ChatTab.Fixed)
            AddTab(tab);

        AddButton = new IconButton(ThemeRoles.IconChatAdd, new Rectangle(0, 0, MainHudLayout.CHAT_TAB_HEIGHT, MainHudLayout.CHAT_TAB_HEIGHT))
        {
            Name = "add_tab_button"
        };

        AddButton.Clicked += () => AddTabRequested?.Invoke();
        AddChild(AddButton);

        HideButton = new IconButton(ThemeRoles.IconChatHide, MainHudLayout.ChatHideButton) { Name = "hide_button" };
        HideButton.Clicked += () => SetCollapsed(!IsCollapsed);
        AddChild(HideButton);

        View = new ChatLogView(log, MainHudLayout.ChatLog);
        AddChild(View);

        //a click does not focus the input; the chat keys do, and then it takes the mouse
        Input = new ChatInputControl(MainHudLayout.ChatInput)
        {
            TextStyle = View,
            IsHitTestVisible = false
        };

        Input.FocusChanged += focused =>
        {
            Input.IsHitTestVisible = focused;
            LayOut();
        };

        AddChild(Input);

        Log.EntryAdded += OnEntryAdded;
        Select(ChatTab.Chat.Name);
        LayOut();
    }

    /// <summary>The scrollback of the selected tab.</summary>
    internal ChatLogView View { get; }

    /// <summary>The chat input of the box.</summary>
    public ChatInputControl Input { get; }

    /// <summary>The tab that shows.</summary>
    internal ChatTab SelectedTab { get; private set; } = ChatTab.Chat;

    /// <summary>True while the box shows only its tab row.</summary>
    public bool IsCollapsed { get; private set; }

    /// <summary>The name of the selected tab, which the world screen saves.</summary>
    public string SelectedTabName => SelectedTab.Name;

    /// <summary>The tabs, in the order of the row.</summary>
    internal IEnumerable<ChatTab> TabList => Tabs.Select(tab => tab.Tab);

    /// <summary>The custom tabs, in the order of the row, which the world screen saves.</summary>
    public IReadOnlyList<ChatTab> CustomTabs => Tabs.Skip(ChatTab.Fixed.Count).Select(tab => tab.Tab).ToList();

    /// <summary>True while the row has space for another custom tab: the <c>+</c> button shows.</summary>
    public bool CanAddTab => Tabs.Count - ChatTab.Fixed.Count < MAX_CUSTOM_TABS;

    /// <summary>The <c>+</c> button after the tabs.</summary>
    internal IconButton AddTabButton => AddButton;

    /// <summary>The controls of the tabs, in the order of the row.</summary>
    internal IEnumerable<SelectableTab> TabControls => Tabs.Select(tab => tab.Control);

    /// <summary>True when the tab named <paramref name="name" /> shows its mark.</summary>
    internal bool IsMarked(string name) => Tabs.Any(tab => (tab.Tab.Name == name) && tab.Control.IsMarked);

    /// <summary>
    ///     Raised when the player selects a tab, hides the box or shows it, or adds, changes or removes a custom tab, so
    ///     that the world screen saves the state.
    /// </summary>
    public event Action? StateChanged;

    /// <summary>Raised on a click on the <c>+</c> button: the world screen opens the editor for a new tab.</summary>
    public event Action? AddTabRequested;

    /// <summary>Raised on a right click on a custom tab, with the screen point: the world screen opens its menu (Edit, Remove).</summary>
    public event Action<ChatTab, int, int>? TabMenuRequested;

    /// <summary>
    ///     True when no tab but <paramref name="except" /> has the name <paramref name="name" />, in any case, so that each
    ///     name identifies one tab.
    /// </summary>
    public bool IsNameFree(string name, ChatTab? except = null)
        => !Tabs.Any(tab => (tab.Tab != except) && string.Equals(tab.Tab.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The editor's OK: a new tab when <paramref name="edited" /> is null, else a change of <paramref name="edited" />.</summary>
    public void Apply(ChatTab? edited, ChatTab tab)
    {
        if (edited is null)
            AddCustomTab(tab);
        else
            ReplaceCustomTab(edited, tab);
    }

    /// <summary>Puts the saved custom tabs in the row: the valid ones with a free name, up to the limit. Raises no event.</summary>
    public void SetCustomTabs(IEnumerable<ChatTab> tabs)
    {
        foreach (var tab in CustomTabs)
            RemoveTab(tab);

        foreach (var tab in tabs)
            if (CanAddTab && tab.IsValidCustom && IsNameFree(tab.Name))
                AddTab(tab);

        Select(SelectedTab.Name);
        LayOut();
    }

    /// <summary>Adds a custom tab at the end of the row and selects it.</summary>
    public void AddCustomTab(ChatTab tab)
    {
        if (!CanAddTab || !tab.IsValidCustom || !IsNameFree(tab.Name))
            return;

        AddTab(tab);
        Select(tab.Name);
        LayOut();
        StateChanged?.Invoke();
    }

    /// <summary>Changes the name or the channels of a custom tab, in its place; a selected tab stays selected.</summary>
    public void ReplaceCustomTab(ChatTab tab, ChatTab updated)
    {
        var index = CustomIndex(tab);

        if ((index < 0) || !updated.IsValidCustom || !IsNameFree(updated.Name, tab))
            return;

        //the mark was for the old channels
        var control = Tabs[index].Control;
        Tabs[index] = (updated, control);
        control.SetText(updated.Name);
        control.IsMarked = false;

        if (SelectedTab == tab)
        {
            SelectedTab = updated;
            View.ShowTab(updated);
        }

        LayOut();
        StateChanged?.Invoke();
    }

    /// <summary>Removes a custom tab; when it was selected, the Chat tab is selected.</summary>
    public void RemoveCustomTab(ChatTab tab)
    {
        if (CustomIndex(tab) < 0)
            return;

        RemoveTab(tab);

        if (SelectedTab == tab)
            Select(ChatTab.Chat.Name);

        LayOut();
        StateChanged?.Invoke();
    }

    /// <summary>Selects the tab named <paramref name="name" />, or the Chat tab when no tab has the name.</summary>
    public void Select(string name)
    {
        var index = Tabs.FindIndex(tab => tab.Tab.Name == name);

        if (index < 0)
            index = Tabs.FindIndex(tab => tab.Tab == ChatTab.Chat);

        SelectedTab = Tabs[index].Tab;

        for (var i = 0; i < Tabs.Count; i++)
            Tabs[i].Control.IsSelected = i == index;

        if (!IsCollapsed)
            Tabs[index].Control.IsMarked = false;

        View.ShowTab(SelectedTab);
    }

    /// <summary>Hides the box to its tab row, or shows it.</summary>
    public void SetCollapsed(bool collapsed)
    {
        IsCollapsed = collapsed;
        View.Visible = !collapsed;
        HideButton.Role = collapsed ? ThemeRoles.IconChatShow : ThemeRoles.IconChatHide;
        IsPassThrough = collapsed;

        //the player sees the selected tab again
        if (!collapsed)
            Tabs.Single(tab => tab.Tab == SelectedTab).Control.IsMarked = false;

        LayOut();
        StateChanged?.Invoke();
    }

    /// <summary>Selects the System tab and shows the box: Shift+F on the new HUD.</summary>
    public void ShowSystem()
    {
        Select(ChatTab.System.Name);
        SetCollapsed(false);
    }

    /// <summary>Scrolls the selected tab by a wheel delta. Returns false when the box is hidden or did not move.</summary>
    public bool Scroll(int delta) => !IsCollapsed && View.Scroll(delta);

    /// <summary>
    ///     True when the box takes the mouse at the screen point: its full area while it shows, else its tab row, and the
    ///     chat input while it has the focus. A drop there does not reach the world.
    /// </summary>
    public bool TakesMouseAt(int screenX, int screenY)
    {
        if (!Visible)
            return false;

        var area = ShownArea();

        return new Rectangle(ScreenX + area.X, ScreenY + area.Y, area.Width, area.Height).Contains(screenX, screenY)
               || (Input.IsHitTestVisible && Input.ScreenBounds.Contains(screenX, screenY));
    }

    public override void Update(GameTime gameTime)
    {
        if (TabLayout.Changed)
            LayOutTabs();

        base.Update(gameTime);
    }

    public override void Dispose()
    {
        Log.EntryAdded -= OnEntryAdded;

        base.Dispose();
    }

    //the flat border is part of the look
    protected internal override Color? DrawnBorderColor => BorderColor is null ? null : ThemeColor(ThemeRoles.Border);

    //the frame covers the full box while it shows, and the tab row alone while it is hidden
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        var area = ShownArea();
        var shown = new Rectangle(bounds.X + area.X, bounds.Y + area.Y, area.Width, area.Height);

        if (TryDrawThemePart(spriteBatch, ThemeRoles.HudChatBox, shown))
            return;

        base.DrawChrome(spriteBatch, shown);
    }

    //the part of the box that draws and takes the mouse, in the coordinates of the box
    private Rectangle ShownArea() => IsCollapsed ? MainHudLayout.ChatCollapsedRow : new Rectangle(0, 0, Width, Height);

    //the strip of the tab row: at the top while the box shows, at the bottom while it is hidden
    private Rectangle RowRect() => IsCollapsed ? MainHudLayout.ChatCollapsedRow : MainHudLayout.ChatShownRow;

    //the places that change with the state: the tab row, the hide button, the input and the line area
    private void LayOut()
    {
        var row = RowRect();
        Backdrop.SetBounds(row);
        HideButton.Y = row.Y + MainHudLayout.CHAT_ROW_PAD;

        Input.Y = IsCollapsed ? row.Y - MainHudLayout.ChatInput.Height - 2 : MainHudLayout.ChatInput.Y;

        //the lines go to the bottom of the box, and above the input while it shows
        var bottom = !IsCollapsed && Input.IsFocused ? MainHudLayout.ChatInput.Y - 2 : MainHudLayout.ChatLog.Bottom;
        View.SetLineArea(bottom - MainHudLayout.ChatLog.Y);

        LayOutTabs();
    }

    //the fixed tabs are tab_system, tab_combat and tab_chat. A custom tab is custom_tab_N. The box does not use N again,
    //so a name stays unique after a remove. An edit keeps the control of the tab
    private void AddTab(ChatTab tab)
    {
        var isCustom = Tabs.Count >= ChatTab.Fixed.Count;

        var control = new SelectableTab(tab.Name, 1, MainHudLayout.CHAT_TAB_HEIGHT)
        {
            Name = isCustom ? $"custom_tab_{NextCustomId++}" : $"tab_{tab.Name.ToLowerInvariant()}"
        };

        control.Clicked += () =>
        {
            Select(TabOf(control).Name);
            StateChanged?.Invoke();
        };

        if (isCustom)
            control.RightClicked += (x, y) => TabMenuRequested?.Invoke(TabOf(control), x, y);

        Tabs.Add((tab, control));
        AddChild(control);
    }

    private void RemoveTab(ChatTab tab)
    {
        var index = Tabs.FindIndex(entry => entry.Tab == tab);
        var control = Tabs[index].Control;

        //the element names are unique among the children, and the remove disposes the control
        Tabs.RemoveAt(index);
        RemoveChild(control.Name);
    }

    private ChatTab TabOf(SelectableTab control) => Tabs.Single(entry => entry.Control == control).Tab;

    private int CustomIndex(ChatTab tab)
    {
        var index = Tabs.FindIndex(entry => entry.Tab == tab);

        return index >= ChatTab.Fixed.Count ? index : -1;
    }

    //each tab fits its name at the size the face draws it, and the row shrinks the widest tabs when the names do not fit
    private void LayOutTabs()
    {
        TabLayout.Mark();

        //the + button takes its place after the tabs while the row has space for a custom tab
        AddButton.Visible = CanAddTab;
        var available = MainHudLayout.ChatTabRowWidth - (CanAddTab ? AddButton.Width + MainHudLayout.CHAT_TAB_GAP : 0);

        var preferred = Tabs.Select(tab => tab.Control.MeasureLabel(tab.Tab.Name) + MainHudLayout.CHAT_TAB_PAD).ToList();
        var widths = MainHudLayout.FitTabs(preferred, available, MainHudLayout.CHAT_TAB_GAP);
        var x = MainHudLayout.CHAT_ROW_PAD;
        var y = RowRect().Y + MainHudLayout.CHAT_ROW_PAD;

        for (var i = 0; i < Tabs.Count; i++)
        {
            Tabs[i].Control.SetBounds(new Rectangle(x, y, widths[i], MainHudLayout.CHAT_TAB_HEIGHT));
            x += widths[i] + MainHudLayout.CHAT_TAB_GAP;
        }

        AddButton.SetBounds(new Rectangle(x, y, AddButton.Width, AddButton.Height));
    }

    private void OnEntryAdded(ChatEntry entry)
    {
        foreach (var (tab, control) in Tabs)
            if (((tab != SelectedTab) || IsCollapsed) && tab.Shows(entry))
                control.IsMarked = true;
    }
}
