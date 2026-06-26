#region
using Brigid.Collections;
using Brigid.Controls.World.Hud.Panel.Slots;
using Brigid.Data.Models;
using Brigid.ViewModel;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     Inventory item grid panel (A key). Thin view that subscribes to
///     <see cref="Inventory" /> change events and renders item icons with dye colors.
///     Supports expand toggle that grows the panel upward (3->5 rows in small HUD, 1->5 rows in large HUD).
/// </summary>
public sealed class InventoryPanel : PanelBase
{
    private const int MAX_SLOTS = 59;
    private const ushort GOLD_SPRITE = 136;
    private const int EXPANDED_SLOTS = 5 * DEFAULT_COLUMNS;

    private readonly PanelSlot GoldSlot;
    private long Gold = long.MinValue;

    public InventoryPanel(
        ControlPrefabSet hudPrefabSet,
        Texture2D? background = null,
        Texture2D? expandedBackground = null,
        int normalVisibleSlots = DEFAULT_VISIBLE_SLOTS)
        : base(
            hudPrefabSet,
            MAX_SLOTS,
            gridOffsetX: 7,
            gridOffsetY: 5,
            background: background,
            normalVisibleSlots: normalVisibleSlots)
    {
        Name = "Inventory";

        ConfigureExpand(expandedBackground ?? UiRenderer.Instance!.GetSpfTexture("_ninv5.spf"), EXPANDED_SLOTS);

        //gold bag occupies the last visible grid cell, overlaying the slot at that index
        GoldSlot = new PanelSlot
        {
            Name = "GoldBag",
            Slot = 0,
            Width = ICON_SIZE,
            Height = ICON_SIZE,
            NormalTexture = RenderIcon(GOLD_SPRITE).Texture,
            ZIndex = 1
        };

        GoldSlot.SlotName = $"Gold( {WorldState.Inventory.Gold} )";
        GoldSlot.DragStarted += OnDragStarted;
        AddChild(GoldSlot);

        //position gold at last visible cell and hide the slot underneath
        PositionGoldSlot(normalVisibleSlots - 1);

        //subscribe to state events
        WorldState.Inventory.SlotChanged += OnSlotChanged;
        WorldState.Inventory.GoldChanged += OnGoldChanged;
        WorldState.Inventory.Cleared += OnCleared;
    }

    public override void Dispose()
    {
        WorldState.Inventory.SlotChanged -= OnSlotChanged;
        WorldState.Inventory.GoldChanged -= OnGoldChanged;
        WorldState.Inventory.Cleared -= OnCleared;

        base.Dispose();
    }

    private void OnCleared()
    {
        foreach (var slot in Slots)
        {
            slot.NormalTexture?.Dispose();
            slot.NormalTexture = null;
            slot.SlotName = null;
            slot.CurrentDurability = 0;
            slot.MaxDurability = 0;
        }
    }

    private void OnGoldChanged()
    {
        var gold = (long)WorldState.Inventory.Gold;

        if (gold == Gold)
            return;

        Gold = gold;
        GoldSlot.SlotName = $"Gold( {gold} )";
    }

    private void OnSlotChanged(byte slot)
    {
        var control = FindSlot(slot);

        if (control is null)
            return;

        var data = WorldState.Inventory.GetSlot(slot);

        if (data.IsOccupied)
        {
            control.NormalTexture?.Dispose();
            control.NormalTexture = UiRenderer.Instance!.GetItemIcon(data.Sprite, data.Color);
            control.SlotName = data.Name;
            control.CurrentDurability = data.CurrentDurability;
            control.MaxDurability = data.MaxDurability;
        } else
        {
            control.NormalTexture?.Dispose();
            control.NormalTexture = null;
            control.SlotName = null;
            control.CooldownPercent = 0;
            control.CurrentDurability = 0;
            control.MaxDurability = 0;
        }
    }

    private void PositionGoldSlot(int gridIndex)
    {
        if (gridIndex < Slots.Count)
        {
            GoldSlot.X = Slots[gridIndex].X;
            GoldSlot.Y = Slots[gridIndex].Y;
        } else if (Slots.Count > 0)
        {
            //beyond the last real slot (e.g. 60th cell in 5-row expanded with 59 slots)
            var lastIndex = Slots.Count - 1;
            var colDelta = (gridIndex % Columns) - (lastIndex % Columns);
            var rowDelta = (gridIndex / Columns) - (lastIndex / Columns);

            GoldSlot.X = Slots[lastIndex].X + colDelta * CELL_WIDTH;
            GoldSlot.Y = Slots[lastIndex].Y + rowDelta * CELL_HEIGHT;
        }

        //hide the real slot underneath the gold bag (no-op when gold is beyond real slots)
        for (var i = 0; i < Slots.Count; i++)
            if (i < VisibleSlotCount)
                Slots[i].Visible = i != gridIndex;

        if (gridIndex < Slots.Count)
            Slots[gridIndex].Visible = false;
    }

    //Item icons are legacy-sourced (ItemXXX.epf). No modern pack path exists for items yet, so always legacy offset.
    protected override IconTexture RenderIcon(ushort spriteId) => IconTexture.Legacy(UiRenderer.Instance!.GetItemIcon(spriteId));

    public override void SetExpanded(bool expanded)
    {
        base.SetExpanded(expanded);

        PositionGoldSlot(VisibleSlotCount - 1);
    }

    protected override PanelSlot? FindHoveredSlot(int screenX, int screenY)
    {
        if (GoldSlot.Visible && GoldSlot.ContainsPoint(screenX, screenY))
            return GoldSlot;

        return base.FindHoveredSlot(screenX, screenY);
    }
}