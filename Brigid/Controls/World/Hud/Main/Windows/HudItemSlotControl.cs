#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The base of an item slot of a window of the new HUD (the Inventory slot of BRIG-150, the Equipment slot of
///     BRIG-151): the slot part (else a dark box with a border), the dim of a slot that takes nothing, the item icon or the
///     empty-slot art of the subclass, and the border of a drop target while a drag that the slot takes is over it. A
///     single click does nothing and does not reach the world under the window; a left drag of an item carries the payload
///     of the subclass. The icon is a cached texture of <see cref="UiRenderer" />; the slot does not own it.
/// </summary>
internal abstract class HudItemSlotControl : UIPanel
{
    private const float DIM = 0.6f;

    private bool IsDropTarget;

    /// <param name="usable">False for a slot that takes nothing: it shows dimmed, with no item, and takes no drop.</param>
    protected HudItemSlotControl(bool usable) => Usable = usable;

    /// <summary>False for a slot that takes nothing: it shows dimmed, with no item, and takes no drop.</summary>
    public bool Usable { get; }

    /// <summary>The icon of the item, or null for an empty slot.</summary>
    public IconTexture? Icon { get; private set; }

    internal bool ShowsDropTarget => IsDropTarget;

    /// <summary>The slot part of the theme.</summary>
    protected abstract string SlotRole { get; }

    /// <summary>The part that the theme draws when it has no <see cref="SlotRole" />, or null.</summary>
    protected virtual string? FallbackSlotRole => null;

    /// <summary>The icon of an item sprite in a colour, from <see cref="UiRenderer" />; null before the renderer exists.</summary>
    public static IconTexture? ItemIcon(ushort sprite, DisplayColor color) => UiRenderer.Instance?.GetItemIcon(sprite, color);

    /// <summary>Shows the item <paramref name="icon" />, or an empty slot for null; a slot that takes nothing shows none.</summary>
    protected void SetIcon(IconTexture? icon) => Icon = Usable ? icon : null;

    /// <summary>True for a drag that this slot takes: the slot shows the drop border while the drag is over it.</summary>
    protected abstract bool TakesDrop(object? payload);

    /// <summary>The payload of a left drag of the item <paramref name="icon" />.</summary>
    protected abstract object DragPayloadOf(IconTexture icon);

    /// <summary>A drop on the slot. The drop border ends before it.</summary>
    protected abstract void HandleDrop(DragDropEvent e);

    /// <summary>The pointer came on the slot (true) or went off it (false).</summary>
    protected abstract void OnPointer(bool entered);

    /// <summary>Draws the art of an empty slot; none by default.</summary>
    protected virtual void DrawEmpty(SpriteBatch spriteBatch, Rectangle bounds) { }

    //a single click does nothing, and does not reach the world under the window
    public override void OnClick(ClickEvent e)
    {
        if (e.Button == MouseButton.Left)
            e.Handled = true;
    }

    public override void OnDragStart(DragStartEvent e)
    {
        if ((e.Button == MouseButton.Left) && Icon is { } icon)
            e.Payload = DragPayloadOf(icon);
    }

    public override void OnDragMove(DragMoveEvent e) => IsDropTarget = Usable && TakesDrop(e.Payload);

    //a drag can end with no drop and no leave (a release that a modal blocks): the border ends with the drag
    public override void Update(GameTime gameTime)
    {
        if (IsDropTarget && !TakesDrop(InputDispatcher.Instance?.ActiveDragPayload))
            IsDropTarget = false;

        base.Update(gameTime);
    }

    public override void OnDragDrop(DragDropEvent e)
    {
        IsDropTarget = false;
        HandleDrop(e);
    }

    public override void OnMouseEnter()
    {
        base.OnMouseEnter();
        OnPointer(true);
    }

    public override void OnMouseLeave()
    {
        base.OnMouseLeave();
        IsDropTarget = false;
        OnPointer(false);
    }

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (!TryDrawThemePart(spriteBatch, SlotRole, bounds, FallbackSlotRole))
        {
            DrawRectClipped(spriteBatch, bounds, DialogPalette.SlotFill);
            DrawBorderClipped(spriteBatch, bounds, ThemeColor(ThemeRoles.Divider));
        }

        if (Icon is { } icon)
            DrawTexture(spriteBatch, icon.Texture, new Vector2(bounds.X + icon.OffsetX, bounds.Y + icon.OffsetY), Color.White);
        else
            DrawEmpty(spriteBatch, bounds);

        //the dim goes over the icon and the outline, so a slot that takes nothing reads as disabled
        if (!Usable)
            DrawRectClipped(spriteBatch, bounds, Color.Black * DIM);

        if (IsDropTarget)
            DrawBorderClipped(spriteBatch, bounds, ThemeColor(ThemeRoles.TextSelected));
    }
}
