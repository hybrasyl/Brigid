#region
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Components;

// ReSharper disable once ClassCanBeSealed.Global
public class UIImage : UIElement
{
    public Texture2D? Texture { get; set; }

    /// <summary>
    ///     Pixel offset applied to the texture's draw position relative to the element's ScreenX/ScreenY. Used by
    ///     modern-sourced icons (32x32) to align into legacy 31x31 slot layouts by shifting 1px up and 1px left. Default
    ///     <see cref="Vector2.Zero" />.
    /// </summary>
    public Vector2 TextureOffset { get; set; }

    /// <summary>
    ///     Tint color multiplied into the texture at draw time. Default white (no tint). Used for learnable/locked
    ///     ability icon states.
    /// </summary>
    public Color TextureTint { get; set; } = Color.White;

    /// <summary>
    ///     When true, the texture is scaled uniformly (preserving aspect ratio) to fit within the element's bounds
    ///     and centered. Takes precedence over <see cref="TextureOffset" />. Does not participate in the clip helper
    ///     — intended for fully-visible UI slots where the texture is not subject to parent clipping. Default false.
    /// </summary>
    public bool ScaleToFit { get; set; }

    public event Action<UIImage>? Hovered;
    public event Action<UIImage>? Unhovered;

    public override void OnMouseEnter() => Hovered?.Invoke(this);

    public override void OnMouseLeave() => Unhovered?.Invoke(this);

    public override void Dispose()
    {
        Texture?.Dispose();
        Texture = null;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        //always run base.Draw so ClipRect updates for hit-testing — even when Texture is null.
        //a textureless visible image still has bounds and may be hit-tested.
        base.Draw(spriteBatch);

        if (Texture is null)
            return;

        if (ScaleToFit && (Texture.Width > 0) && (Texture.Height > 0))
        {
            var scale = MathF.Min((float)Width / Texture.Width, (float)Height / Texture.Height);
            var scaledW = (int)MathF.Round(Texture.Width * scale);
            var scaledH = (int)MathF.Round(Texture.Height * scale);
            var destRect = new Rectangle(
                ScreenX + ((Width - scaledW) / 2),
                ScreenY + ((Height - scaledH) / 2),
                scaledW,
                scaledH);

            spriteBatch.Draw(Texture, destRect, TextureTint);

            return;
        }

        DrawTexture(
            spriteBatch,
            Texture,
            new Vector2(ScreenX, ScreenY) + TextureOffset,
            TextureTint);
    }

}