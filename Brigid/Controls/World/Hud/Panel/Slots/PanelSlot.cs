#region
using Brigid.Controls.Components;
using Brigid.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel.Slots;

/// <summary>
///     A single slot in an icon grid panel (inventory, skill book, spell book). Extends UIButton with cooldown overlay
///     rendering, double-click detection, and drag-and-drop support. The parent panel creates one PanelSlotControl per
///     visible grid cell and manages layout, slot assignment, and drag state.
/// </summary>
public class PanelSlot : UIButton
{
    private bool DoubleClickFired;

    /// <summary>
    ///     Cooldown progress from 0 (fully cooled down) to 1 (just started, fully on cooldown).
    /// </summary>
    public float CooldownPercent { get; set; }

    /// <summary>
    ///     How the cooldown overlay is rendered.
    /// </summary>
    public CooldownStyle CooldownStyle { get; set; }

    /// <summary>
    ///     Blue-tinted copy of the normal icon used as the cooldown overlay. For <see cref="CooldownStyle.Progressive" />
    ///     this is progressively revealed top-to-bottom over the grey base; for <see cref="CooldownStyle.Swap" /> it
    ///     replaces the normal icon entirely for the duration.
    /// </summary>
    public Texture2D? CooldownTexture { get; set; }

    public int CurrentDurability { get; set; }

    /// <summary>
    ///     Grey-tinted copy of the normal icon shown underneath <see cref="CooldownStyle.Progressive" /> cooldowns.
    /// </summary>
    public Texture2D? GreyTexture { get; set; }

    public bool IsDropTarget { get; set; }

    public int MaxDurability { get; set; }

    /// <summary>
    ///     The 1-based slot number this control represents.
    /// </summary>
    public byte Slot { get; init; }

    /// <summary>
    ///     Display name of the item/skill/spell in this slot. Used for hover tooltips by the parent.
    /// </summary>
    public string? SlotName { get; set; }

    public override void Dispose()
    {
        CooldownTexture?.Dispose();
        CooldownTexture = null;
        GreyTexture?.Dispose();
        GreyTexture = null;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        if (IsDropTarget)
        {
            DrawBorder(
                spriteBatch,
                new Rectangle(
                    ScreenX,
                    ScreenY,
                    Width,
                    Height),
                Color.Black);

            DrawBorder(
                spriteBatch,
                new Rectangle(
                    ScreenX + 1,
                    ScreenY + 1,
                    Width - 2,
                    Height - 2),
                Color.Black);

            DrawBorder(
                spriteBatch,
                new Rectangle(
                    ScreenX + 2,
                    ScreenY + 2,
                    Width - 4,
                    Height - 4),
                Color.Black);
        }

        //icon rendering with cooldown overlay
        var icon = NormalTexture;

        if (icon is null)
            return;

        //apply icon offset (e.g. -1/-1 for modern 32x32 icons into legacy 31x31 slots)
        var pos = new Vector2(ScreenX, ScreenY) + TextureOffset;

        if ((CooldownPercent > 0) && CooldownTexture is not null)
            switch (CooldownStyle)
            {
                case CooldownStyle.Swap:
                    DrawTexture(
                        spriteBatch,
                        CooldownTexture,
                        pos,
                        Color.White);

                    break;

                case CooldownStyle.Progressive:
                    //retail parity: grey-tinted base + blue-tinted top-revealed overlay (SkillInvItemPane::Render)
                    DrawTexture(
                        spriteBatch,
                        GreyTexture ?? icon,
                        pos,
                        Color.White);

                    var elapsed = 1f - CooldownPercent;
                    var revealHeight = (int)(CooldownTexture.Height * elapsed);

                    if (revealHeight > 0)
                    {
                        var srcRect = new Rectangle(
                            0,
                            0,
                            CooldownTexture.Width,
                            revealHeight);

                        DrawTexture(
                            spriteBatch,
                            CooldownTexture,
                            pos,
                            srcRect,
                            Color.White);
                    }

                    break;

                default:
                    DrawTexture(
                        spriteBatch,
                        icon,
                        pos,
                        Color.White);

                    break;
            }
        else
            DrawTexture(
                spriteBatch,
                icon,
                pos,
                Color.White);
    }

    /// <summary>
    ///     Fired on double-click. Parameter is the 1-based slot number.
    /// </summary>
    public event PanelSlotDoubleClickedHandler? DoubleClicked;

    /// <summary>
    ///     Fired when a drag begins on this slot. Parameter is the 1-based slot number.
    /// </summary>
    public event PanelSlotDragStartedHandler? DragStarted;

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button == MouseButton.Left)
            DoubleClickFired = false;

        base.OnMouseDown(e);
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button == MouseButton.Left)
            e.Handled = true;
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        if ((e.Button == MouseButton.Left) && NormalTexture is not null && (CooldownPercent <= 0))
        {
            DoubleClicked?.Invoke(Slot);
            DoubleClickFired = true;
            e.Handled = true;
        }
    }

    public override void OnDragStart(DragStartEvent e)
    {
        if (NormalTexture is null || (CooldownPercent > 0) || DoubleClickFired)
            return;

        e.Payload = new SlotDragPayload
        {
            Source = this,
            SlotIndex = Slot,
            SourcePanel = (Parent as PanelBase)?.Tab ?? default
        };

        DragStarted?.Invoke(this);
    }

    public override void OnDragMove(DragMoveEvent e)
    {
        if (e.Payload is SlotDragPayload payload && (payload.Source.Parent == Parent))
            IsDropTarget = true;
    }

    public override void OnMouseLeave()
    {
        base.OnMouseLeave();
        IsDropTarget = false;
        (Parent as PanelBase)?.ForceHoverExit();
    }

    public override void OnDragDrop(DragDropEvent e)
    {
        IsDropTarget = false;

        if (e.Payload is not SlotDragPayload payload)
            return;

        //only accept drops from slots within the same parent panel
        if (Parent is not PanelBase panel || (payload.Source.Parent != Parent))
            return;

        //dropping on the same slot is a no-op — just end drag
        if (payload.SlotIndex == Slot)
        {
            panel.EndDrag();
            e.Handled = true;

            return;
        }

        panel.CompleteDragSwap(Slot);
        e.Handled = true;
    }

}