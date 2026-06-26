#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Components;

public class UIButton : UIElement
{
    /// <summary>
    ///     When true, textures smaller than the element's Width/Height are drawn centered. The element stays the size of the
    ///     largest texture. Used for buttons with differently-sized textures per state (e.g. status book tabs: small normal,
    ///     big selected).
    /// </summary>
    public bool CenterTexture { get; set; }

    public Texture2D? DisabledTexture { get; set; }
    public Texture2D? HoverTexture { get; set; }
    public bool IsHovered { get; private set; }
    public string? TooltipText { get; set; }
    public bool IsPressed { get; private set; }
    public bool IsSelected { get; set; }
    public Texture2D? NormalTexture { get; set; }
    public Texture2D? PressedTexture { get; set; }
    public Texture2D? SelectedTexture { get; set; }

    /// <summary>
    ///     Pixel offset applied to all of the button's texture draw positions. Used for ability icon slots that may
    ///     host a modern 32x32 icon needing -1/-1 alignment into a legacy 31x31 slot. Default <see cref="Vector2.Zero" />
    ///     preserves legacy layout exactly.
    /// </summary>
    public Vector2 TextureOffset { get; set; }

    private Texture2D? ActiveTexture
    {
        get
        {
            if (!Enabled && DisabledTexture is not null)
                return DisabledTexture;

            if (IsSelected && SelectedTexture is not null)
                return SelectedTexture;

            // ReSharper disable once ConvertIfStatementToSwitchStatement
            if (Enabled && IsPressed && PressedTexture is not null)
                return PressedTexture;

            if (Enabled && IsHovered && HoverTexture is not null)
                return HoverTexture;

            return NormalTexture;
        }
    }

    public override void Dispose()
    {
        //selectedtexture may share the same object as pressedtexture — only dispose if distinct
        if (SelectedTexture is not null && (SelectedTexture != PressedTexture))
            SelectedTexture.Dispose();

        SelectedTexture = null;

        NormalTexture?.Dispose();
        NormalTexture = null;
        PressedTexture?.Dispose();
        PressedTexture = null;
        HoverTexture?.Dispose();
        HoverTexture = null;
        DisabledTexture?.Dispose();
        DisabledTexture = null;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var texture = ActiveTexture;

        if (texture is null)
            return;

        var drawX = ScreenX;
        var drawY = ScreenY;

        if (CenterTexture)
        {
            drawX += (Width - texture.Width) / 2;
            drawY += (Height - texture.Height) / 2;
        }

        DrawTexture(
            spriteBatch,
            texture,
            new Vector2(drawX, drawY) + TextureOffset,
            Color.White);
    }

    public event ClickedHandler? Clicked;
    public event ClickedWithModifiersHandler? ClickedWithModifiers;
    public event HoveredHandler? Hovered;
    public event UnhoveredHandler? Unhovered;

    public void PerformClick() => Clicked?.Invoke();

    public override void ResetInteractionState()
    {
        IsHovered = false;
        IsPressed = false;
        IsSelected = false;
    }

    public override void OnMouseEnter()
    {
        IsHovered = true;
        Hovered?.Invoke(this);
    }

    public override void OnMouseLeave()
    {
        IsHovered = false;
        IsPressed = false;
        Unhovered?.Invoke(this);
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if (e.Button == MouseButton.Left)
        {
            IsPressed = true;
            e.Handled = true;
        }
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if (e.Button == MouseButton.Left)
            IsPressed = false;
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button == MouseButton.Left)
        {
            Clicked?.Invoke();
            ClickedWithModifiers?.Invoke(e.Modifiers);
            e.Handled = true;
        }
    }
}