#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Systems.Keybinds;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The windows of the new HUD (BRIG-127 D3, D29, D30): the row of window buttons from <see cref="HudWindows" />, the
///     panels of the windows that open in the HUD, and the actions that the world screen binds to the other windows. A
///     button and a key reach a window through one call (<see cref="Toggle" />). Each button shows selected while its window
///     is open, by one rule (<see cref="RefreshSelection" />).
/// </summary>
/// <remarks>
///     A window card puts its content in its panel with <see cref="BindPanel" />. A window that opens on a server reply
///     (the mail, the Aisling list) calls <see cref="Open" /> from the reply. The HUD adds <see cref="Row" /> and
///     <see cref="Panels" /> as its children; the panels are not in the row, so the row does not clip them.
/// </remarks>
internal sealed class HudWindowHost
{
    private readonly HudPanelHost Host = new();
    private readonly HudTooltip Tooltip;
    private readonly Dictionary<HudWindow, IconButton> Buttons = [];
    private readonly Dictionary<HudWindow, HudPanel> PanelOf = [];
    private readonly Dictionary<HudWindow, WindowBinding> Bindings = [];
    private readonly Dictionary<HudWindow, PanelBinding> PanelBindings = [];

    //a window that the world screen owns: its click, how its button knows that it is open, its right click, its tooltip
    private sealed record WindowBinding(Action Toggle, Func<bool>? IsOpen, Action? RightClick, Func<IEnumerable<TooltipLine>>? Lines);

    //what a window card does when its panel opens and closes
    private sealed record PanelBinding(Action? Opened, Action? Closed);

    public HudWindowHost(HudTooltip tooltip)
    {
        Tooltip = tooltip;
        Row = new UIPanel { Name = "window_buttons", IsPassThrough = true };
        Row.SetBounds(MainHudLayout.WindowButtons);

        for (var i = 0; i < HudWindows.All.Length; i++)
        {
            var window = HudWindows.All[i];
            var bounds = MainHudLayout.WindowButtonAt(i, HudWindows.All.Length);
            var button = new IconButton(window.IconRole, bounds with { X = bounds.X - Row.X, Y = bounds.Y - Row.Y }) { Name = window.Id };

            button.Clicked += () => Toggle(window);
            button.RightClicked += () => RightClick(window);
            button.Entered += hovered => Tooltip.ShowAbove(hovered, () => Lines(window));
            button.Left += Tooltip.Hide;
            Buttons[window] = button;
            Row.AddChild(button);

            if (window.Panel is not null)
            {
                var panel = new HudPanel(window);
                panel.CloseRequested += closed => Close(closed.Window);
                panel.SwapSidesRequested += SwapSides;
                PanelOf[window] = panel;
            }
        }
    }

    /// <summary>The row of window buttons.</summary>
    public UIPanel Row { get; }

    /// <summary>The panels of the windows that open in the HUD, hidden until they open.</summary>
    public IEnumerable<HudPanel> Panels => PanelOf.Values;

    /// <summary>The panel of a panel window.</summary>
    public HudPanel PanelFor(HudWindow window) => PanelOf[window];

    /// <summary>The button of <paramref name="window" />.</summary>
    public IconButton ButtonOf(HudWindow window) => Buttons[window];

    /// <summary>
    ///     Binds a window that the world screen owns to its button: a click runs <paramref name="toggle" />, the button shows
    ///     selected while <paramref name="isOpen" /> is true, a right click runs <paramref name="rightClick" />, and the
    ///     tooltip adds <paramref name="lines" /> under the name.
    /// </summary>
    public void BindWindow(
        HudWindow window,
        Action toggle,
        Func<bool>? isOpen = null,
        Action? rightClick = null,
        Func<IEnumerable<TooltipLine>>? lines = null)
    {
        Require(window, panel: false);
        Bindings[window] = new WindowBinding(toggle, isOpen, rightClick, lines);
    }

    /// <summary>
    ///     Puts a window card's <paramref name="content" /> in the panel of <paramref name="window" />, in place of the line
    ///     that says the window is not built. <paramref name="opened" /> and <paramref name="closed" /> run when the panel
    ///     opens and closes (a request to the server, for example).
    /// </summary>
    public void BindPanel(HudWindow window, UIElement content, Action? opened = null, Action? closed = null)
    {
        Require(window, panel: true);
        PanelOf[window].SetContent(content);
        PanelBindings[window] = new PanelBinding(opened, closed);
    }

    /// <summary>A click on the button of <paramref name="window" />, or its key: opens or closes the window.</summary>
    public void Toggle(HudWindow window)
    {
        if (window.Panel is { } size)
        {
            Host.Toggle(window.Id, size);
            PlacePanels();
        } else if (Bindings.TryGetValue(window, out var binding))
            binding.Toggle();
    }

    /// <summary>Opens the panel of <paramref name="window" />, or keeps it open (for a window that opens on a server reply).</summary>
    public void Open(HudWindow window)
    {
        Require(window, panel: true);
        Host.Open(window.Id, window.Panel!.Value);
        PlacePanels();
    }

    public void Close(HudWindow window)
    {
        Require(window, panel: true);
        Host.Close(window.Id);
        PlacePanels();
    }

    /// <summary>The swap button of a compact panel: the compact panels change sides.</summary>
    public void SwapSides()
    {
        if (Host.SwapSides())
            PlacePanels();
    }

    /// <summary>Closes every panel. Returns true when a panel was open, so that Escape closes the panels first.</summary>
    public bool CloseAll()
    {
        if (!Host.CloseAll())
            return false;

        PlacePanels();

        return true;
    }

    /// <summary>True when an open panel is at the screen point, so that a click or a drop there does not reach the world.</summary>
    public bool IsOverPanel(int screenX, int screenY)
        => PanelOf.Values.Any(panel => panel.Visible && panel.ScreenBounds.Contains(screenX, screenY));

    /// <summary>
    ///     Each button shows selected while its window is open: a panel while the host has it open, a bound window while its
    ///     binding says so. The HUD calls it at each update, because a bound window can open or close elsewhere.
    /// </summary>
    public void RefreshSelection()
    {
        foreach (var (window, button) in Buttons)
            button.IsSelected = window.Panel is not null
                ? Host.IsOpen(window.Id)
                : Bindings.TryGetValue(window, out var binding) && (binding.IsOpen?.Invoke() ?? false);
    }

    private void RightClick(HudWindow window)
    {
        if (Bindings.TryGetValue(window, out var binding))
            binding.RightClick?.Invoke();
    }

    //each panel shows at its place, or hides; a panel that opens or closes runs its binding
    private void PlacePanels()
    {
        foreach (var (window, panel) in PanelOf)
        {
            var place = Host.PlaceOf(window.Id);
            var wasOpen = panel.Visible;

            if (place is { } bounds)
                panel.SetBounds(bounds);

            panel.Visible = place is not null;

            if ((panel.Visible != wasOpen) && PanelBindings.TryGetValue(window, out var binding))
                (panel.Visible ? binding.Opened : binding.Closed)?.Invoke();
        }

        RefreshSelection();
    }

    //the name, the lines of the binding, and the key as the keybinds resolve it now
    private IReadOnlyList<TooltipLine> Lines(HudWindow window)
    {
        List<TooltipLine> lines = [TooltipLine.Themed(window.Name, ThemeRoles.Text)];

        if (Bindings.TryGetValue(window, out var binding) && binding.Lines is { } extra)
            lines.AddRange(extra());

        var key = window.Key is { } command ? KeybindCatalog.Format(Keybinds.Effective(command)) : window.FixedKey;

        if (key is not null)
            lines.Add(TooltipLine.Themed($"Key: {key}", ThemeRoles.TextDisabled));

        return lines;
    }

    //a window must be in the list, and of the kind that the call is for
    private void Require(HudWindow window, bool panel)
    {
        if (!Buttons.ContainsKey(window))
            throw new ArgumentException($"{window.Id} is not a window of the HUD", nameof(window));

        if ((window.Panel is not null) != panel)
            throw new ArgumentException(
                panel ? $"{window.Id} is not a panel window" : $"{window.Id} is a panel window; bind its content with BindPanel",
                nameof(window));
    }
}
