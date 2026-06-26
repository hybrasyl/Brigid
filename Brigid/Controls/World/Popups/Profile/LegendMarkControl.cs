#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     A single legend mark row with an icon (UIImage) and text label (UILabel). The text is vertically centered relative
///     to the icon.
/// </summary>
public sealed class LegendMarkControl : UIPanel
{
    private const int ICON_TEXT_GAP = 5;

    private readonly UIImage Icon;
    private readonly UILabel Label;

    public LegendMarkControl()
    {
        Icon = new UIImage
        {
            Name = "MarkIcon"
        };

        Label = new UILabel
        {
            Name = "MarkText"
        };

        AddChild(Icon);
        AddChild(Label);
    }

    public void Clear()
    {
        Icon.Texture = null;
        Label.Text = string.Empty;
    }

    public void SetMark(
        Texture2D? icon,
        string text,
        Color color,
        int iconWidth,
        int iconHeight)
    {
        Icon.Texture = icon;
        Icon.Width = iconWidth;
        Icon.Height = iconHeight;

        Label.X = iconWidth + ICON_TEXT_GAP;
        Label.Y = (iconHeight - TextRenderer.CHAR_HEIGHT) / 2;
        Label.Width = Width - Label.X;
        Label.Height = TextRenderer.CHAR_HEIGHT;
        Label.Text = text;
        Label.ForegroundColor = color;
    }

}