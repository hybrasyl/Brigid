#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Data.Models;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     Events/quest tab page (_nui_ev). Three display pages, each with two columns (EV1 left, EV2 right).
///     Each column corresponds to one SEvent file (circle level). NEXT/PREV buttons cycle through pages.
///     Each row uses the _nui_ski prefab with a leicon.epf state icon (completed/available/unavailable).
///     Layout: Page 0 = SEvent1|SEvent2, Page 1 = SEvent3|SEvent4, Page 2 = SEvent5|SEvent6+7.
/// </summary>
public sealed class SelfProfileEventMetadataTab : PrefabPanel
{
    private const int ROW_HEIGHT = 45;
    private const int MAX_VISIBLE_ROWS = 5;
    private const int DISPLAY_ROWS = MAX_VISIBLE_ROWS + 1;
    private const int MAX_DISPLAY_PAGES = 3;
    private const int COLUMNS_PER_PAGE = 2;
    private const int MAX_DISPLAY_SLOTS = MAX_DISPLAY_PAGES * COLUMNS_PER_PAGE;

    //6 display slots (3 pages x 2 columns), each holding events for that slot
    private readonly List<EventMetadataEntry>[] DisplaySlots = new List<EventMetadataEntry>[MAX_DISPLAY_SLOTS];
    private readonly UIPanel LeftContainer;
    private readonly Rectangle LeftRect;

    private readonly EventMetadataEntryControl[] LeftRows;
    private readonly ScrollBarBinder LeftBinder;
    private readonly ScrollModel LeftModel = new();
    private readonly ScrollBarControl LeftScrollBar;
    private readonly UIButton? NextButton;
    private readonly UIButton? PrevButton;
    private readonly UIPanel RightContainer;
    private readonly Rectangle RightRect;
    private readonly EventMetadataEntryControl[] RightRows;
    private readonly ScrollBarBinder RightBinder;
    private readonly ScrollModel RightModel = new();
    private readonly ScrollBarControl RightScrollBar;

    private BaseClass BaseClass;
    private HashSet<string> CompletedEventIds = new(StringComparer.OrdinalIgnoreCase);
    private int CurrentPage;
    private bool Dirty = true;
    private bool EnableMasterQuests;

    public SelfProfileEventMetadataTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        VisibilityChanged += visible =>
        {
            if (visible)
                Dirty = true;
        };

        for (var i = 0; i < MAX_DISPLAY_SLOTS; i++)
            DisplaySlots[i] = [];

        LeftRect = GetRect("EV1");
        RightRect = GetRect("EV2");

        if (LeftRect == Rectangle.Empty)
            LeftRect = new Rectangle(
                32,
                33,
                233,
                239);

        if (RightRect == Rectangle.Empty)
            RightRect = new Rectangle(
                331,
                33,
                233,
                239);

        LeftContainer = new UIPanel
        {
            Name = "LeftContainer",
            X = LeftRect.X,
            Y = LeftRect.Y,
            Width = LeftRect.Width,
            Height = LeftRect.Height,
            IsPassThrough = true
        };

        AddChild(LeftContainer);

        RightContainer = new UIPanel
        {
            Name = "RightContainer",
            X = RightRect.X,
            Y = RightRect.Y,
            Width = RightRect.Width,
            Height = RightRect.Height,
            IsPassThrough = true
        };

        AddChild(RightContainer);

        LeftRows = CreateColumn(LeftContainer, LeftRect.Height);
        RightRows = CreateColumn(RightContainer, RightRect.Height);

        LeftScrollBar = CreateScrollBar(LeftRect);
        RightScrollBar = CreateScrollBar(RightRect);

        LeftBinder = new ScrollBarBinder(LeftModel, LeftScrollBar);
        RightBinder = new ScrollBarBinder(RightModel, RightScrollBar);
        LeftModel.Changed += _ => Dirty = true;
        RightModel.Changed += _ => Dirty = true;

        NextButton = CreateButton("NEXT");
        PrevButton = CreateButton("PREV");

        if (NextButton is not null)
            NextButton.Clicked += () =>
            {
                if (CurrentPage < (MAX_DISPLAY_PAGES - 1))
                {
                    CurrentPage++;
                    LeftModel.ScrollToStart();
                    RightModel.ScrollToStart();
                    SetBackgroundFrame(CurrentPage);
                    Dirty = true;
                }
            };

        if (PrevButton is not null)
            PrevButton.Clicked += () =>
            {
                if (CurrentPage > 0)
                {
                    CurrentPage--;
                    LeftModel.ScrollToStart();
                    RightModel.ScrollToStart();
                    SetBackgroundFrame(CurrentPage);
                    Dirty = true;
                }
            };
    }

    /// <summary>
    ///     Clears all event entries.
    /// </summary>
    public void ClearAll()
    {
        for (var i = 0; i < MAX_DISPLAY_SLOTS; i++)
            DisplaySlots[i] = [];

        CompletedEventIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        CurrentPage = 0;
        LeftModel.SetMetrics(0, MAX_VISIBLE_ROWS);
        LeftModel.ScrollToStart();
        RightModel.SetMetrics(0, MAX_VISIBLE_ROWS);
        RightModel.ScrollToStart();
        SetBackgroundFrame(0);
        Dirty = true;
    }

    private EventMetadataEntryControl[] CreateColumn(UIPanel container, int columnHeight)
    {
        var rows = new EventMetadataEntryControl[DISPLAY_ROWS];

        for (var i = 0; i < DISPLAY_ROWS; i++)
        {
            var row = new EventMetadataEntryControl
            {
                X = 0,
                Y = i * ROW_HEIGHT,
                Visible = false
            };

            //clip the peek row's hit-test area to the column bounds
            var maxHeight = columnHeight - row.Y;

            if (maxHeight < row.Height)
                row.Height = maxHeight;

            row.OnClicked += (entry, state) => OnEntryClicked?.Invoke(entry, state);
            container.AddChild(row);
            rows[i] = row;
        }

        return rows;
    }

    private ScrollBarControl CreateScrollBar(Rectangle columnRect)
    {
        var scrollBar = new ScrollBarControl
        {
            X = columnRect.X + columnRect.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = columnRect.Y,
            Height = columnRect.Height
        };

        AddChild(scrollBar);

        return scrollBar;
    }

    /// <summary>
    ///     Fired when any entry row is clicked. Passes the entry and its resolved state.
    /// </summary>
    public event EventMetadataClickedHandler? OnEntryClicked;

    private void RefreshColumn(EventMetadataEntryControl[] rows, IReadOnlyList<EventMetadataEntry> entries, int scrollOffset)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            var entryIndex = scrollOffset + i;

            if (entryIndex < entries.Count)
            {
                var entry = entries[entryIndex];
                var state = ResolveEventState(entry);

                rows[i]
                    .SetEntry(entry, state);
            } else
                rows[i]
                    .Clear();
        }
    }

    private void RefreshRows()
    {
        if (!Dirty)
            return;

        Dirty = false;
        UpdateScrollBars();

        var leftSlot = CurrentPage * COLUMNS_PER_PAGE;
        var rightSlot = leftSlot + 1;

        var leftEntries = leftSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[leftSlot] : [];
        var rightEntries = rightSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[rightSlot] : [];

        RefreshColumn(LeftRows, leftEntries, LeftModel.Offset);

        RefreshColumn(RightRows, rightEntries, RightModel.Offset);
    }

    private EventState ResolveEventState(EventMetadataEntry entry)
    {
        //completed: player has a legend mark with key matching this event's id
        if (!string.IsNullOrEmpty(entry.Id) && CompletedEventIds.Contains(entry.Id))
            return EventState.Completed;

        var attrs = WorldState.Attributes.Current;
        var playerLevel = attrs?.Level ?? 1;

        //derive player's circle from level; master flag overrides to circle 6
        var playerCircle = EnableMasterQuests
            ? 6
            : playerLevel switch
            {
                >= 99 => 5,
                >= 71 => 4,
                >= 41 => 3,
                >= 11 => 2,
                _     => 1
            };

        //check qualifying circles — player's circle must be in the list
        if (!string.IsNullOrEmpty(entry.QualifyingCircles))
        {
            var circleChar = (char)('0' + playerCircle);

            if (!entry.QualifyingCircles.Contains(circleChar))
                return EventState.Unavailable;
        }

        //check qualifying classes — player's class must be in the list
        if (!string.IsNullOrEmpty(entry.QualifyingClasses))
        {
            var classChar = (char)('0' + (int)BaseClass);

            if (!entry.QualifyingClasses.Contains(classChar))
                return EventState.Unavailable;
        }

        //check prerequisite event — must be completed
        if (!string.IsNullOrEmpty(entry.PreRequisiteId) && !CompletedEventIds.Contains(entry.PreRequisiteId))
            return EventState.Unavailable;

        return EventState.Available;
    }

    /// <summary>
    ///     Sets the event entries from parsed metadata, distributed into display slots.
    /// </summary>
    public void SetEvents(
        IReadOnlyList<EventMetadataEntry> events,
        HashSet<string> completedEventIds,
        BaseClass baseClass,
        bool enableMasterQuests)
    {
        CompletedEventIds = completedEventIds;
        BaseClass = baseClass;
        EnableMasterQuests = enableMasterQuests;

        for (var i = 0; i < MAX_DISPLAY_SLOTS; i++)
            DisplaySlots[i] = [];

        //distribute events to display slots: sevent page → slot = min(page - 1, 5)
        //slot layout: 0=sevent1, 1=sevent2, 2=sevent3, 3=sevent4, 4=sevent5, 5=sevent6+7
        foreach (var entry in events)
        {
            var slotIndex = Math.Min(entry.Page - 1, MAX_DISPLAY_SLOTS - 1);

            if (slotIndex < 0)
                slotIndex = 0;

            DisplaySlots[slotIndex]
                .Add(entry);
        }

        CurrentPage = 0;
        LeftModel.ScrollToStart();
        RightModel.ScrollToStart();
        SetBackgroundFrame(0);
        UpdateScrollBars();
        Dirty = true;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        var leftSlot = CurrentPage * COLUMNS_PER_PAGE;
        var rightSlot = leftSlot + 1;
        var leftCount = leftSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[leftSlot].Count : 0;
        var rightCount = rightSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[rightSlot].Count : 0;

        if (LeftContainer.ContainsPoint(e.ScreenX, e.ScreenY) && (leftCount > MAX_VISIBLE_ROWS))
        {
            LeftModel.WheelBy(e.Delta);
            e.Handled = true;
        } else if (RightContainer.ContainsPoint(e.ScreenX, e.ScreenY) && (rightCount > MAX_VISIBLE_ROWS))
        {
            RightModel.WheelBy(e.Delta);
            e.Handled = true;
        }
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        RefreshRows();
        base.Update(gameTime);
    }

    private void UpdateScrollBars()
    {
        var leftSlot = CurrentPage * COLUMNS_PER_PAGE;
        var rightSlot = leftSlot + 1;

        var leftCount = leftSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[leftSlot].Count : 0;
        var rightCount = rightSlot < MAX_DISPLAY_SLOTS ? DisplaySlots[rightSlot].Count : 0;

        LeftModel.SetMetrics(leftCount, MAX_VISIBLE_ROWS);
        RightModel.SetMetrics(rightCount, MAX_VISIBLE_ROWS);
    }
}