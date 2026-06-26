#region
using Brigid.Controls.Components;
using Brigid.Rendering.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.ViewPort;

/// <summary>
///     A single clickable node on the world map. Renders a small bordered box on the left followed by a text label.
///     Changes text color on hover.
/// </summary>
public sealed class WorldMapNode : UIPanel
{
    private static readonly Color BOX_BORDER_COLOR = new(100, 149, 237);
    private static readonly Color HOVER_TEXT_COLOR = new(247, 142, 24);
    private const int BOX_SIZE = 12;
    private const int BOX_SHRINK = 1;
    private const int BOX_BORDER_WIDTH = 3;
    private const int BOX_GAP = 3;
    private const int GLYPH_HEIGHT = 12;

    private static Texture2D? SharedNormalBox;
    private static Texture2D? SharedHoveredBox;

    private readonly UILabel Label;
    private bool IsHovered;
    public ushort CheckSum { get; }
    public int DestX { get; }
    public int DestY { get; }
    public ushort MapId { get; }

    public int NodeIndex { get; }

    public WorldMapNode(
        int nodeIndex,
        string text,
        ushort mapId,
        int destX,
        int destY,
        ushort checkSum)
    {
        NodeIndex = nodeIndex;
        MapId = mapId;
        DestX = destX;
        DestY = destY;
        CheckSum = checkSum;

        EnsureBoxTextures();

        var textWidth = TextRenderer.MeasureWidth(text);
        Width = BOX_SIZE + BOX_GAP + textWidth;
        Height = Math.Max(BOX_SIZE, GLYPH_HEIGHT);

        Label = new UILabel
        {
            Name = "NodeLabel",
            X = BOX_SIZE + BOX_GAP,
            Y = Height - GLYPH_HEIGHT,
            Width = textWidth,
            Height = GLYPH_HEIGHT,
            Text = text,
            ForegroundColor = Color.White,
            PaddingLeft = 0,
            PaddingRight = 0
        };

        AddChild(Label);
    }

    /// <summary>
    ///     Tests if a screen point is within the box area (full BOX_SIZE, not shrunk).
    /// </summary>
    public bool ContainsBoxPoint(int screenX, int screenY)
    {
        var boxX = ScreenX;
        var boxY = ScreenY + Height - BOX_SIZE;

        return (screenX >= boxX) && (screenX < (boxX + BOX_SIZE)) && (screenY >= boxY) && (screenY < (boxY + BOX_SIZE));
    }

    private static Texture2D CreateBorderTexture(int size)
        => ImageUtil.BuildFilledBorder(ChaosGame.Device, size, BOX_BORDER_WIDTH, BOX_BORDER_COLOR);

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        var bottom = ScreenY + Height;
        var boxTexture = IsHovered ? SharedHoveredBox : SharedNormalBox;

        if (boxTexture is not null)
        {
            //normal box bottom-aligns with text; hovered box shrinks toward its center
            var offsetX = (BOX_SIZE - boxTexture.Width) / 2;
            var offsetY = (BOX_SIZE - boxTexture.Height) / 2;
            var boxY = bottom - BOX_SIZE + offsetY;
            DrawTexture(spriteBatch, boxTexture, new Vector2(ScreenX + offsetX, boxY), Color.White);
        }

        //children (label) drawn by base
        base.Draw(spriteBatch);
    }

    private static void EnsureBoxTextures()
    {
        if (SharedNormalBox is not null)
            return;

        SharedNormalBox = CreateBorderTexture(BOX_SIZE);
        SharedHoveredBox = CreateBorderTexture(BOX_SIZE - BOX_SHRINK * 2);
    }

    public void SetHovered(bool hovered)
    {
        if (hovered == IsHovered)
            return;

        IsHovered = hovered;
        Label.ForegroundColor = hovered ? HOVER_TEXT_COLOR : Color.White;
    }

}