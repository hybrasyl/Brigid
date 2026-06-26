#region
using Brigid.Controls.Components;
using Brigid.Rendering.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Popups.Dialog;

/// <summary>
///     Vertical darkness gradient overlay for NPC dialogs. Covers the bottom portion of the screen (y=274 to y=372),
///     fading linearly from slightly-dark to near-opaque black over a 98px height.
/// </summary>
public sealed class DialogAlphaGradient : UIElement
{
    private static Texture2D? GradientTexture;

    public DialogAlphaGradient()
    {
        X = 0;
        Y = 274;
        Width = 640;
        Height = 98;
        IsHitTestVisible = false;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        EnsureGradientTexture();

        spriteBatch.Draw(
            GradientTexture!,
            new Rectangle(
                ScreenX,
                ScreenY,
                Width,
                Height),
            Color.White);
    }

    private static void EnsureGradientTexture()
    {
        if (GradientTexture is not null)
            return;
        const int HEIGHT = 98;
        GradientTexture = ImageUtil.BuildVerticalAlphaGradient(ChaosGame.Device, HEIGHT, Color.Black, 8, 255);
    }

}