#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Data.Models;
using Brigid.ViewModel;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     Skills tab page (_nui_sk). Two-column layout: SPELL (left) and SKILL (right). Each column
///     holds rows of AbilityEntryControl instances with a scrollbar on the right edge. When more
///     entries exist below the visible area, the top of the next entry peeks at the bottom.
/// </summary>
public sealed class SelfProfileAbilityMetadataTab : PrefabPanel
{
    private const int ROW_HEIGHT = 45;
    private const int MAX_VISIBLE_ROWS = 5;

    //one extra row for the peek effect at the bottom of each column
    private const int DISPLAY_ROWS = MAX_VISIBLE_ROWS + 1;

    private readonly UIPanel SkillContainer;
    private readonly Rectangle SkillRect;

    private readonly AbilityMetadataEntryControl[] SkillRows;
    private readonly ScrollBarBinder SkillBinder;
    private readonly ScrollModel SkillModel = new();
    private readonly ScrollBarControl SkillScrollBar;
    private readonly UIPanel SpellContainer;
    private readonly Rectangle SpellRect;
    private readonly AbilityMetadataEntryControl[] SpellRows;
    private readonly ScrollBarBinder SpellBinder;
    private readonly ScrollModel SpellModel = new();
    private readonly ScrollBarControl SpellScrollBar;

    private bool Dirty = true;
    private IReadOnlyList<AbilityMetadataEntry> SkillEntries = [];
    private IReadOnlyList<AbilityMetadataEntry> SpellEntries = [];

    public SelfProfileAbilityMetadataTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        VisibilityChanged += visible =>
        {
            if (visible)
                Dirty = true;
        };

        SpellRect = GetRect("SPELL");
        SkillRect = GetRect("SKILL");

        if (SpellRect == Rectangle.Empty)
            SpellRect = new Rectangle(
                32,
                33,
                233,
                239);

        if (SkillRect == Rectangle.Empty)
            SkillRect = new Rectangle(
                331,
                33,
                233,
                239);

        SpellContainer = new UIPanel
        {
            Name = "SpellContainer",
            X = SpellRect.X,
            Y = SpellRect.Y,
            Width = SpellRect.Width,
            Height = SpellRect.Height,
            IsPassThrough = true
        };

        AddChild(SpellContainer);

        SkillContainer = new UIPanel
        {
            Name = "SkillContainer",
            X = SkillRect.X,
            Y = SkillRect.Y,
            Width = SkillRect.Width,
            Height = SkillRect.Height,
            IsPassThrough = true
        };

        AddChild(SkillContainer);

        SpellRows = CreateColumn(SpellContainer, SpellRect.Height);
        SkillRows = CreateColumn(SkillContainer, SkillRect.Height);

        SpellScrollBar = CreateScrollBar(SpellRect);
        SkillScrollBar = CreateScrollBar(SkillRect);

        SpellBinder = new ScrollBarBinder(SpellModel, SpellScrollBar);
        SkillBinder = new ScrollBarBinder(SkillModel, SkillScrollBar);
        SpellModel.Changed += _ => Dirty = true;
        SkillModel.Changed += _ => Dirty = true;
    }

    /// <summary>
    ///     Clears all entries from both columns.
    /// </summary>
    public void ClearAll()
    {
        SpellEntries = [];
        SkillEntries = [];
        SpellModel.SetMetrics(0, MAX_VISIBLE_ROWS);
        SpellModel.ScrollToStart();
        SkillModel.SetMetrics(0, MAX_VISIBLE_ROWS);
        SkillModel.ScrollToStart();
        Dirty = true;
    }

    private AbilityMetadataEntryControl[] CreateColumn(UIPanel container, int columnHeight)
    {
        var rows = new AbilityMetadataEntryControl[DISPLAY_ROWS];

        for (var i = 0; i < DISPLAY_ROWS; i++)
        {
            var row = new AbilityMetadataEntryControl
            {
                X = 0,
                Y = i * ROW_HEIGHT,
                Visible = false
            };

            //clip the peek row's hit-test area to the column bounds
            var maxHeight = columnHeight - row.Y;

            if (maxHeight < row.Height)
                row.Height = maxHeight;

            row.OnClicked += entry => OnEntryClicked?.Invoke(entry);
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

    private static bool HasPreRequisite(string? name, byte requiredLevel)
    {
        if (name is null)
            return true;

        //check spell book
        for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
        {
            ref readonly var slot = ref WorldState.SpellBook.GetSlot(i);

            if (slot.IsOccupied && (slot.AbilityName?.EqualsI(name) == true) && (slot.CurrentLevel >= requiredLevel))
                return true;
        }

        //check skill book
        for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
        {
            ref readonly var slot = ref WorldState.SkillBook.GetSlot(i);

            if (slot.IsOccupied && (slot.AbilityName?.EqualsI(name) == true) && (slot.CurrentLevel >= requiredLevel))
                return true;
        }

        return false;
    }

    /// <summary>
    ///     Fired when any entry row is clicked.
    /// </summary>
    public event AbilityMetadataClickedHandler? OnEntryClicked;

    private static void RefreshColumn(AbilityMetadataEntryControl[] rows, IReadOnlyList<AbilityMetadataEntry> entries, int scrollOffset)
    {
        for (var i = 0; i < rows.Length; i++)
        {
            var entryIndex = scrollOffset + i;

            if (entryIndex < entries.Count)
            {
                var entry = entries[entryIndex];
                var state = ResolveIconState(entry);

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

        RefreshColumn(SpellRows, SpellEntries, SpellModel.Offset);

        RefreshColumn(SkillRows, SkillEntries, SkillModel.Offset);
    }

    private static AbilityIconState ResolveIconState(AbilityMetadataEntry entry)
    {
        //check if player already knows this ability
        if (entry.IsSpell)
            for (byte i = 1; i <= SpellBook.MAX_SLOTS; i++)
            {
                ref readonly var slot = ref WorldState.SpellBook.GetSlot(i);

                if (slot.IsOccupied && (slot.AbilityName?.EqualsI(entry.Name) == true))
                    return AbilityIconState.Known;
            }
        else
            for (byte i = 1; i <= SkillBook.MAX_SLOTS; i++)
            {
                ref readonly var slot = ref WorldState.SkillBook.GetSlot(i);

                if (slot.IsOccupied && (slot.AbilityName?.EqualsI(entry.Name) == true))
                    return AbilityIconState.Known;
            }

        //check if player meets the requirements to learn it
        if (WorldState.Attributes.Current is not { } attrs)
            return AbilityIconState.Locked;

        if (attrs.Level < entry.Level)
            return AbilityIconState.Locked;

        if ((attrs.Str < entry.Str)
            || (attrs.Int < entry.Int)
            || (attrs.Wis < entry.Wis)
            || (attrs.Dex < entry.Dex)
            || (attrs.Con < entry.Con))
            return AbilityIconState.Locked;

        //check prerequisite abilities
        if (!HasPreRequisite(entry.PreReq1Name, entry.PreReq1Level))
            return AbilityIconState.Locked;

        if (!HasPreRequisite(entry.PreReq2Name, entry.PreReq2Level))
            return AbilityIconState.Locked;

        return AbilityIconState.Learnable;
    }

    /// <summary>
    ///     Populates both columns from parsed ability metadata.
    /// </summary>
    public void SetAbilityMetadata(AbilityMetadata metadata)
    {
        SpellEntries = metadata.Spells;
        SkillEntries = metadata.Skills;

        SpellModel.SetMetrics(SpellEntries.Count, MAX_VISIBLE_ROWS);
        SpellModel.ScrollToStart();
        SkillModel.SetMetrics(SkillEntries.Count, MAX_VISIBLE_ROWS);
        SkillModel.ScrollToStart();
        Dirty = true;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (SpellContainer.ContainsPoint(e.ScreenX, e.ScreenY) && SpellModel.CanScroll)
        {
            SpellModel.WheelBy(e.Delta);
            e.Handled = true;
        } else if (SkillContainer.ContainsPoint(e.ScreenX, e.ScreenY) && SkillModel.CanScroll)
        {
            SkillModel.WheelBy(e.Delta);
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

}