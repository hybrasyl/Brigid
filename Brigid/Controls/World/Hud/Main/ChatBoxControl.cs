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
///         The box is translucent: the <see cref="ThemeRoles.HudChatBox" /> part, else the flat frame fill with alpha.
///         While it shows, it takes the mouse, so a click on the chat does not walk; collapsed, only its tab row takes
///         it.
///     </para>
/// </summary>
public sealed class ChatBoxControl : UIPanel
{
    //the alpha of the flat fill: the world shows through the box
    private const byte FLAT_ALPHA = 150;

    private readonly UIPanel Backdrop;
    private readonly IconButton HideButton;
    private readonly Chat Log;
    private readonly FontGenerationStamp TabLayout = new();
    private readonly List<(ChatTab Tab, SelectableTab Control)> Tabs = [];

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

    /// <summary>The controls of the tabs, in the order of the row.</summary>
    internal IEnumerable<SelectableTab> TabControls => Tabs.Select(tab => tab.Control);

    /// <summary>True when the tab named <paramref name="name" /> shows its mark.</summary>
    internal bool IsMarked(string name) => Tabs.Any(tab => (tab.Tab.Name == name) && tab.Control.IsMarked);

    /// <summary>Raised when the player selects a tab, hides the box or shows it, so that the world screen saves the state.</summary>
    public event Action? StateChanged;

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

    private void AddTab(ChatTab tab)
    {
        var control = new SelectableTab(tab.Name, 1, MainHudLayout.CHAT_TAB_HEIGHT) { Name = $"tab_{tab.Name.ToLowerInvariant()}" };

        control.Clicked += () =>
        {
            Select(tab.Name);
            StateChanged?.Invoke();
        };

        Tabs.Add((tab, control));
        AddChild(control);
    }

    //each tab fits its name at the size the face draws it, and the row shrinks the widest tabs when the names do not fit
    private void LayOutTabs()
    {
        TabLayout.Mark();

        var preferred = Tabs.Select(tab => TextRenderer.MeasureWidth(tab.Tab.Name) + MainHudLayout.CHAT_TAB_PAD).ToList();
        var widths = MainHudLayout.FitTabs(preferred, MainHudLayout.ChatTabRowWidth, MainHudLayout.CHAT_TAB_GAP);
        var x = MainHudLayout.CHAT_ROW_PAD;
        var y = RowRect().Y + MainHudLayout.CHAT_ROW_PAD;

        for (var i = 0; i < Tabs.Count; i++)
        {
            Tabs[i].Control.SetBounds(new Rectangle(x, y, widths[i], MainHudLayout.CHAT_TAB_HEIGHT));
            x += widths[i] + MainHudLayout.CHAT_TAB_GAP;
        }
    }

    private void OnEntryAdded(ChatEntry entry)
    {
        foreach (var (tab, control) in Tabs)
            if (((tab != SelectedTab) || IsCollapsed) && tab.Shows(entry))
                control.IsMarked = true;
    }
}
