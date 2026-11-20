#region
using System.Globalization;
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The base of a slot of the new HUD that shows an icon (the Inventory slot of BRIG-150, the Equipment slot of BRIG-151,
///     the hotbar slot of BRIG-146): the slot part (else a dark box with a border), the icon or the empty-slot art of the
///     subclass, the count of a stack for a subclass that shows one, what the subclass draws over the icon
///     (<see cref="DrawOverIcon" />), the dim (<see cref="Dimmed" />, by default a slot that takes nothing), and the border
///     of a drop target while a drag that the slot takes is over it. By default a single click does nothing and does not reach the world under the slot; a left drag of an icon carries the
///     payload of the subclass. The icon is a cached texture of <see cref="UiRenderer" />; the slot does not own it.
/// </summary>
internal abstract class HudItemSlotControl : UIPanel
{
    private const float DIM = 0.6f;
    private const int COUNT_HEIGHT = 12;

    private readonly UILabel? CountText;
    private bool IsDropTarget;
    private uint ShownCount;

    /// <param name="usable">False for a slot that takes nothing: it shows dimmed, with no item, and takes no drop.</param>
    /// <param name="showsCount">True for a slot that shows the count of a stack at the bottom right (<see cref="ShowCount" />).</param>
    protected HudItemSlotControl(bool usable, bool showsCount = false)
    {
        Usable = usable;

        if (!showsCount)
            return;

        CountText = HudText.Label("count", ThemeRoles.Text, HorizontalAlignment.Right);
        AddChild(CountText);
    }

    /// <summary>False for a slot that takes nothing: it shows dimmed, with no item, and takes no drop.</summary>
    public bool Usable { get; }

    /// <summary>The icon that the slot shows, or null for an empty slot.</summary>
    public IconTexture? Icon { get; private set; }

    internal bool ShowsDropTarget => IsDropTarget;

    /// <summary>The count that the slot shows, or an empty string.</summary>
    internal string CountShown => CountText?.Text ?? string.Empty;

    /// <summary>The slot part of the theme.</summary>
    protected abstract string SlotRole { get; }

    /// <summary>The part that the theme draws when it has no <see cref="SlotRole" />, or null.</summary>
    protected virtual string? FallbackSlotRole => null;

    /// <summary>The icon of an item sprite in a colour, from <see cref="UiRenderer" />; null before the renderer exists.</summary>
    public static IconTexture? ItemIcon(ushort sprite, DisplayColor color) => UiRenderer.Instance?.GetItemIcon(sprite, color);

    /// <summary>Shows <paramref name="icon" />, or an empty slot for null; a slot that takes nothing shows none.</summary>
    protected void SetIcon(IconTexture? icon) => Icon = Usable ? icon : null;

    /// <summary>Shows <paramref name="count" /> at the bottom right; a count of 1 or less does not show.</summary>
    protected void ShowCount(uint count)
    {
        if ((CountText is null) || (count == ShownCount))
            return;

        ShownCount = count;
        CountText.Text = count > 1 ? count.ToString(CultureInfo.InvariantCulture) : string.Empty;
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        CountText?.SetBounds(new Rectangle(0, bounds.Height - COUNT_HEIGHT, bounds.Width - 1, COUNT_HEIGHT));
    }

    /// <summary>True for a drag that this slot takes: the slot shows the drop border while the drag is over it.</summary>
    protected abstract bool TakesDrop(object? payload);

    /// <summary>The payload of a left drag of the slot that shows <paramref name="icon" />.</summary>
    protected abstract object DragPayloadOf(IconTexture icon);

    /// <summary>A drop on the slot. The drop border ends before it.</summary>
    protected abstract void HandleDrop(DragDropEvent e);

    /// <summary>The pointer came on the slot (true) or went off it (false).</summary>
    protected abstract void OnPointer(bool entered);

    /// <summary>Draws the art of an empty slot; none by default.</summary>
    protected virtual void DrawEmpty(SpriteBatch spriteBatch, Rectangle bounds) { }

    /// <summary>Draws over the icon, under the dim (the cooldown of a hotbar slot); nothing by default.</summary>
    protected virtual void DrawOverIcon(SpriteBatch spriteBatch, Rectangle bounds) { }

    /// <summary>True when the slot shows dimmed: by default a slot that takes nothing.</summary>
    protected virtual bool Dimmed => !Usable;

    //a single click does nothing, and does not reach the world under the slot
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
        DrawSlotBox(spriteBatch, bounds, SlotRole, FallbackSlotRole);

        if (Icon is { } icon)
            DrawTexture(spriteBatch, icon.Texture, icon.PositionIn(bounds), Color.White);
        else
            DrawEmpty(spriteBatch, bounds);

        DrawOverIcon(spriteBatch, bounds);

        //the dim goes over the icon and the outline, so a slot that takes nothing, or a gone hotbar entry, reads as dimmed
        if (Dimmed)
            DrawRectClipped(spriteBatch, bounds, Color.Black * DIM);

        if (IsDropTarget)
            DrawBorderClipped(spriteBatch, bounds, ThemeColor(ThemeRoles.TextSelected));
    }
}
