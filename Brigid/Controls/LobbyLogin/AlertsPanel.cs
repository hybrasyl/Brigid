#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>
///     The main menu's local alerts (BRIG-107, BRIG-87): a framed panel with one tab for each <see cref="MenuAlert" />, a
///     next arrow, the selected alert's markdown body and its action buttons. It is not modal and takes no keys, so the
///     menu stays usable while an alert waits. The panel hides itself when it has no alerts.
///     <para>
///         The panel is as tall as the selected alert needs, between <see cref="MIN_HEIGHT" /> and
///         <see cref="MAX_HEIGHT" />, and keeps its bottom edge at <see cref="Bottom" />. A body taller than the limit
///         scrolls with the mouse wheel. When the tabs do not all fit, the row shows the window of tabs that holds the
///         selected one, and the next arrow moves through all of them.
///     </para>
/// </summary>
public sealed class AlertsPanel : FramePanel
{
    public const int PANEL_WIDTH = 360;
    public const int MIN_HEIGHT = 110;
    public const int MAX_HEIGHT = 300;

    private const int PADDING = 8;

    //the tab row and the next arrow sit clear of the frame corners
    private const int TAB_ROW_X = 14;
    private const int TAB_ROW_Y = 12;
    private const int TAB_WIDTH = 92;
    private const int TAB_HEIGHT = 18;
    private const int TAB_GAP = 4;
    private const int NEXT_WIDTH = 22;
    private const int ACTION_WIDTH = 88;
    private const int ACTION_HEIGHT = 20;
    private const int ACTION_GAP = 6;
    private const int BODY_TOP = TAB_ROW_Y + TAB_HEIGHT + PADDING;

    //smaller than the reading size of MarkdownView: the panel is small and sits beside the menu
    private const int TEXT_SIZE = 10;


    private static readonly MenuAlertAction DefaultAction = new("Dismiss");

    private readonly List<MenuAlert> Items = [];
    private readonly MarkdownBlock Body;
    private readonly TextButton NextButton;
    private readonly UIPanel Actions;

    private TabStrip? Tabs;

    //what the tab row and the action row were made for; they are made again only when it changes
    private string[] TabTitles = [];
    private MenuAlert? ActionsFor;

    //the index of the first tab in the row
    private int WindowStart;

    //the edge the body and the action row were last placed for
    private int LaidOutEdge = -1;

    /// <summary>The alerts, in the order they were added.</summary>
    public IReadOnlyList<MenuAlert> Alerts => Items;

    /// <summary>The index of the selected alert, or -1 when there are no alerts.</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <summary>
    ///     True while a popup covers the menu, for example the server greeting. The panel then stays hidden, also with
    ///     alerts, and shows again when the popup closes.
    /// </summary>
    public bool Covered
    {
        get;
        set
        {
            field = value;
            Visible = (Items.Count > 0) && !value;
        }
    }

    /// <summary>The bottom edge of the panel in its parent. The panel grows up from it.</summary>
    public int Bottom
    {
        get;
        set
        {
            field = value;
            Y = value - Height;
        }
    }

    /// <summary>The number of tabs that fit in the row beside the next arrow.</summary>
    public static int TabCapacity => Math.Max(1, (PANEL_WIDTH - 2 * TAB_ROW_X - NEXT_WIDTH) / (TAB_WIDTH + TAB_GAP));

    public AlertsPanel()
    {
        Name = "alerts";
        Width = PANEL_WIDTH;
        Height = MIN_HEIGHT;
        Visible = false;

        NextButton = new TextButton(">", NEXT_WIDTH, TAB_HEIGHT)
        {
            Name = "next",
            X = Width - TAB_ROW_X - NEXT_WIDTH,
            Y = TAB_ROW_Y
        };

        NextButton.Clicked += SelectNext;
        AddChild(NextButton);

        Body = new MarkdownBlock
        {
            Name = "body",
            X = PADDING,
            Y = BODY_TOP,
            Width = Width - 2 * PADDING,
            Height = MIN_HEIGHT - BODY_TOP - PADDING / 2 - ACTION_HEIGHT - PADDING,
            TextSize = TEXT_SIZE,
            IsHitTestVisible = false
        };

        Body.LaidOut += FitToBody;
        AddChild(Body);

        Actions = new UIPanel
        {
            Name = "actions",
            X = PADDING,
            Y = MIN_HEIGHT - PADDING - ACTION_HEIGHT,
            Width = Width - 2 * PADDING,
            Height = ACTION_HEIGHT,
            IsPassThrough = true
        };

        AddChild(Actions);
        Refresh();
    }

    /// <summary>
    ///     Adds an alert, or replaces the alert with the same id, and shows the panel. An alert with no actions gets a
    ///     Dismiss button, so that the player can always close it.
    /// </summary>
    public void Add(MenuAlert alert)
    {
        if (alert.Actions.Count == 0)
            alert = alert with { Actions = [DefaultAction] };

        var index = Items.FindIndex(item => item.Id == alert.Id);

        if (index >= 0)
            Items[index] = alert;
        else
            Items.Add(alert);

        if (SelectedIndex < 0)
            SelectedIndex = 0;

        Refresh();
    }

    /// <summary>Removes the alert with this id. Returns false when there is no such alert.</summary>
    public bool Remove(string id)
    {
        var index = Items.FindIndex(item => item.Id == id);

        if (index < 0)
            return false;

        Items.RemoveAt(index);

        //the selection stays at the same place in the row, or moves to the new last alert
        if ((index < SelectedIndex) || (SelectedIndex >= Items.Count))
            SelectedIndex--;

        if (Items.Count == 0)
            SelectedIndex = -1;

        Refresh();

        return true;
    }

    /// <summary>Selects an alert. An index out of range changes nothing.</summary>
    public void Select(int index)
    {
        if ((index < 0) || (index >= Items.Count) || (index == SelectedIndex))
            return;

        SelectedIndex = index;
        Refresh();
    }

    /// <summary>Selects the next alert, and the first after the last.</summary>
    public void SelectNext()
    {
        if (Items.Count > 1)
            Select((SelectedIndex + 1) % Items.Count);
    }

    /// <summary>
    ///     Does an action of the selected alert: the alert goes away, then the action runs. The alert goes first, so an
    ///     action can add a new alert with the same id.
    /// </summary>
    public void RunAction(int actionIndex)
    {
        if ((SelectedIndex < 0) || (actionIndex < 0) || (actionIndex >= Items[SelectedIndex].Actions.Count))
            return;

        var alert = Items[SelectedIndex];

        Remove(alert.Id);
        alert.Actions[actionIndex].Run?.Invoke();
    }

    //the wheel scrolls a body that is taller than the panel can grow
    public override void OnMouseScroll(MouseScrollEvent e)
    {
        Body.ScrollY = Math.Clamp(Body.ScrollY - e.Delta * TextRenderer.CHAR_HEIGHT, 0, MaxScroll);
        e.Handled = true;
    }

    private int MaxScroll => Math.Max(0, Body.ContentHeight - Body.Height);

    //the distance from the panel edge to the body and the action row: more inside a themed frame whose border is wider
    private int Edge => InsetFor(PADDING);

    //the space that the body does not use: the tab row above it and the action row below it
    private int ChromeHeight => BODY_TOP + PADDING / 2 + ACTION_HEIGHT + Edge;

    //a panel becomes themed when it is added to the menu, after its constructor, so the edge is checked on each change
    private bool ApplyEdge()
    {
        if (Edge == LaidOutEdge)
            return false;

        LaidOutEdge = Edge;
        Body.X = Edge;
        Body.Width = Width - 2 * Edge;
        Actions.X = Edge;
        Actions.Width = Width - 2 * Edge;

        return true;
    }

    private void Refresh()
    {
        Visible = (Items.Count > 0) && !Covered;
        NextButton.Visible = Items.Count > 1;

        //keep the selected tab inside the row
        var capacity = TabCapacity;

        if (SelectedIndex < WindowStart)
            WindowStart = Math.Max(0, SelectedIndex);
        else if (SelectedIndex >= WindowStart + capacity)
            WindowStart = SelectedIndex - capacity + 1;

        WindowStart = Math.Clamp(WindowStart, 0, Math.Max(0, Items.Count - capacity));

        RefreshTabs(capacity);

        var selected = SelectedIndex >= 0 ? Items[SelectedIndex] : null;
        var edgeChanged = ApplyEdge();

        if (!ReferenceEquals(selected, ActionsFor))
        {
            ActionsFor = selected;
            RebuildActions(selected);
            Body.ScrollY = 0;
            Body.SetMarkdown(selected?.Body);
        } else if (edgeChanged)
        {
            RebuildActions(selected);
            Body.EnsureLayout();
        }
    }

    //the strip is made again only when the titles in the row change; a new selection inside the row only moves it
    private void RefreshTabs(int capacity)
    {
        var titles = Items.Skip(WindowStart)
                          .Take(capacity)
                          .Select(item => item.Title)
                          .ToArray();

        if (Tabs is null || !titles.SequenceEqual(TabTitles))
        {
            if (Tabs is not null)
                RemoveChild(Tabs.Name);

            TabTitles = titles;

            Tabs = new TabStrip(titles, TAB_WIDTH, TAB_HEIGHT, TAB_GAP)
            {
                Name = "tabs",
                X = TAB_ROW_X,
                Y = TAB_ROW_Y
            };

            Tabs.TabClicked += index => Select(WindowStart + index);
            AddChild(Tabs);
        }

        Tabs.SetSelected(SelectedIndex - WindowStart);
    }

    private void RebuildActions(MenuAlert? alert)
    {
        foreach (var child in Actions.Children)
            child.Dispose();

        Actions.Children.Clear();

        if (alert is null)
            return;

        var list = alert.Actions;

        //right-aligned, in their order from left to right
        var x = Actions.Width - list.Count * ACTION_WIDTH - (list.Count - 1) * ACTION_GAP;

        for (var i = 0; i < list.Count; i++)
        {
            var index = i;

            var button = new TextButton(list[i].Text, ACTION_WIDTH, ACTION_HEIGHT)
            {
                Name = $"action_{i}",
                X = x
            };

            button.Clicked += () => RunAction(index);
            Actions.AddChild(button);
            x += ACTION_WIDTH + ACTION_GAP;
        }
    }

    //each new layout of the body sets the panel height, so a long alert is not cut off
    private void FitToBody()
    {
        Height = Math.Clamp(Body.ContentHeight + ChromeHeight, MIN_HEIGHT, MAX_HEIGHT);
        Body.Height = Height - ChromeHeight;
        Actions.Y = Height - Edge - ACTION_HEIGHT;
        Y = Bottom - Height;
        Body.ScrollY = Math.Min(Body.ScrollY, MaxScroll);
    }
}
