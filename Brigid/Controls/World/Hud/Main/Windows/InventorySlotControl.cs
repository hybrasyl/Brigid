#region
using System.Globalization;
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     A slot of the Inventory window (BRIG-150): the <see cref="ThemeRoles.HudInventorySlot" /> part, else a dark box with
///     a border; the icon of the item; and the count of a stack of more than one at the bottom right. The icon is a cached
///     texture of <see cref="UiRenderer" />; the slot does not own it. On an item a double click raises
///     <see cref="UseRequested" />, as on the classic panel, and a press and a move start a drag with an
///     <see cref="ItemDragPayload" />; a drop of an item from another slot raises <see cref="SwapRequested" />. A slot past
///     the inventory size of today is dimmed and takes no item and no drop.
/// </summary>
internal sealed class InventorySlotControl : UIPanel
{
    private const float DIM = 0.6f;
    private const int COUNT_HEIGHT = 12;

    private readonly UILabel CountText;
    private bool DoubleClickFired;
    private bool IsDropTarget;

    /// <param name="slot">The inventory slot, from 1.</param>
    /// <param name="usable">False for a slot past the inventory size of today.</param>
    public InventorySlotControl(byte slot, bool usable)
    {
        Slot = slot;
        Usable = usable;
        Name = $"slot_{slot}";

        CountText = HudText.Label("count", ThemeRoles.Text, HorizontalAlignment.Right);
        AddChild(CountText);
    }

    /// <summary>The inventory slot, from 1.</summary>
    public byte Slot { get; }

    /// <summary>False for a slot past the inventory size of today: it shows dimmed and takes no drop.</summary>
    public bool Usable { get; }

    /// <summary>The icon of the item, or null for an empty slot.</summary>
    public IconTexture? Icon { get; private set; }

    internal string CountShown => CountText.Text;

    internal bool ShowsDropTarget => IsDropTarget;

    /// <summary>A double click on an item: use it.</summary>
    public event Action<byte>? UseRequested;

    /// <summary>An item of the first slot dropped on the second slot.</summary>
    public event Action<byte, byte>? SwapRequested;

    /// <summary>The pointer came on the slot (true) or went off it (false).</summary>
    public event Action<byte, bool>? PointerChanged;

    /// <summary>Shows the item <paramref name="icon" />, or an empty slot for null, with the count of a stack.</summary>
    /// <param name="count">The count to show; a count of 1 or less does not show.</param>
    public void Show(IconTexture? icon, uint count)
    {
        Icon = Usable ? icon : null;
        CountText.Text = Icon is not null && (count > 1) ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        CountText.SetBounds(new Rectangle(0, bounds.Height - COUNT_HEIGHT, bounds.Width - 1, COUNT_HEIGHT));
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button == MouseButton.Left)
            DoubleClickFired = false;

        base.OnMouseDown(e);
    }

    //a single click does nothing, and does not reach the world under the window
    public override void OnClick(ClickEvent e)
    {
        if (e.Button == MouseButton.Left)
            e.Handled = true;
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        if ((e.Button != MouseButton.Left) || Icon is null)
            return;

        UseRequested?.Invoke(Slot);
        DoubleClickFired = true;
        e.Handled = true;
    }

    //the press of a double click does not start a drag, as on the classic panel
    public override void OnDragStart(DragStartEvent e)
    {
        if ((e.Button == MouseButton.Left) && Icon is { } icon && !DoubleClickFired)
            e.Payload = new ItemDragPayload(Slot, icon.Texture);
    }

    public override void OnDragMove(DragMoveEvent e) => IsDropTarget = Usable && e.Payload is ItemDragPayload;

    //a drag can end with no drop and no leave (a release that a modal blocks): the highlight ends with the drag
    public override void Update(GameTime gameTime)
    {
        if (IsDropTarget && InputDispatcher.Instance?.ActiveDragPayload is not ItemDragPayload)
            IsDropTarget = false;

        base.Update(gameTime);
    }

    //a drop of an item on the window ends here, so it does not go to the ground under the window
    public override void OnDragDrop(DragDropEvent e)
    {
        IsDropTarget = false;

        if (e.Payload is not ItemDragPayload item)
            return;

        if (Usable && (item.Slot != Slot))
            SwapRequested?.Invoke(item.Slot, Slot);

        e.Handled = true;
    }

    public override void OnMouseEnter()
    {
        base.OnMouseEnter();
        PointerChanged?.Invoke(Slot, true);
    }

    public override void OnMouseLeave()
    {
        base.OnMouseLeave();
        IsDropTarget = false;
        PointerChanged?.Invoke(Slot, false);
    }

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (!TryDrawThemePart(spriteBatch, ThemeRoles.HudInventorySlot, bounds))
        {
            DrawRectClipped(spriteBatch, bounds, DialogPalette.SlotFill);
            DrawBorderClipped(spriteBatch, bounds, ThemeColor(ThemeRoles.Divider));
        }

        if (Icon is { } icon)
            DrawTexture(spriteBatch, icon.Texture, new Vector2(bounds.X + icon.OffsetX, bounds.Y + icon.OffsetY), Color.White);

        if (IsDropTarget)
            DrawBorderClipped(spriteBatch, bounds, ThemeColor(ThemeRoles.TextSelected));

        if (!Usable)
            DrawRectClipped(spriteBatch, bounds, Color.Black * DIM);
    }
}
