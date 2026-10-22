#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive title: one line of text in the display face (<see cref="FontStyle.Display" />), centred in the element
///     with a drop shadow. When the element is themed and the theme has art for <see cref="ArtRole" />, the art replaces
///     the text. The display face is Uncial Antiqua, or the <c>title</c> font of the theme pack.
/// </summary>
public sealed class DisplayTitle : UIElement
{
    private const int SHADOW_OFFSET = 2;

    /// <summary>The title text.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>The pixel size of the text. The title owns its own line, so it sets an explicit size.</summary>
    public int Size { get; set; } = 32;

    /// <summary>The theme part that replaces the text, or null for text only.</summary>
    public string? ArtRole { get; set; }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        var bounds = new Rectangle(ScreenX, ScreenY, Width, Height);

        if (ArtRole is not null && TryDrawThemePart(spriteBatch, ArtRole, bounds))
            return;

        if (string.IsNullOrEmpty(Text))
            return;

        //measured and drawn at the same size and style, so the centring matches the glyphs
        var engine = FontEngine.Instance;
        var width = engine.MeasureWidth(Text, Size, FontStyle.Display);
        var height = engine.GetLineHeight(Size, FontStyle.Display);
        var position = new Vector2(bounds.X + (bounds.Width - width) / 2f, bounds.Y + (bounds.Height - height) / 2f);

        engine.DrawLine(
            spriteBatch,
            Text,
            position + new Vector2(SHADOW_OFFSET),
            DialogPalette.TitleShadow,
            ClipRect,
            Size,
            FontStyle.Display);

        engine.DrawLine(spriteBatch, Text, position, ThemeColor(ThemeRoles.TextTitle), ClipRect, Size, FontStyle.Display);
    }
}
