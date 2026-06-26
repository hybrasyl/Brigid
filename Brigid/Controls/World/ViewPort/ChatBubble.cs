#region
using Brigid.Controls.Components;
using Brigid.Rendering.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.ViewPort;

/// <summary>
///     Chat bubble rendered as a fixed-width rounded capsule with an optional tail. Width is based on a fixed monospaced
///     character cell width, not measured glyph width.
/// </summary>
public sealed class ChatBubble : UIImage
{
    private const int MAX_LINE_CHARS = 18;
    private const int CHAR_CELL_WIDTH = TextRenderer.CHAR_WIDTH;
    private const int LINE_HEIGHT = TextRenderer.CHAR_HEIGHT;

    private const int LEFT_CAP_WIDTH = 7;
    private const int RIGHT_CAP_WIDTH = 7;
    private const int INNER_PADDING_LEFT = 2;
    private const int INNER_PADDING_RIGHT = 4;
    private const int INNER_PADDING_TOP = 3;
    private const int INNER_PADDING_BOTTOM = 3;
    private const int BUBBLE_HEIGHT = 17;

    private const int TAIL_HEIGHT = 4;

    private const float DISPLAY_DURATION_MS = 3000f;

    private static readonly Color BubbleBorderColor = Color.White;

    private static readonly Color BubbleFillColor = new(
        0,
        0,
        0,
        85);

    private static readonly Color NormalTextColor = LegendColors.White;
    private static readonly Color ShoutTextColor = TextColors.Shout;

    private readonly List<string> Lines;
    private readonly Color TextColor;

    private float ElapsedMs;
    public bool TailOnTop { get; set; }

    public uint EntityId { get; }

    public bool IsExpired => ElapsedMs >= DISPLAY_DURATION_MS;

    private ChatBubble(
        uint entityId,
        Texture2D backgroundTexture,
        List<string> lines,
        Color textColor,
        int width,
        int height)
    {
        EntityId = entityId;
        Texture = backgroundTexture;
        Lines = lines;
        TextColor = textColor;
        Width = width;
        Height = height;
    }

    public static ChatBubble Create(uint entityId, string message, bool isShout)
    {
        var lines = WordWrap(message);
        var textColor = isShout ? ShoutTextColor : NormalTextColor;

        var textAreaWidth = MAX_LINE_CHARS * CHAR_CELL_WIDTH + 2;

        var bubbleWidth = LEFT_CAP_WIDTH + INNER_PADDING_LEFT + textAreaWidth + INNER_PADDING_RIGHT + RIGHT_CAP_WIDTH;

        var bubbleHeight = Math.Max(BUBBLE_HEIGHT, INNER_PADDING_TOP + lines.Count * LINE_HEIGHT + INNER_PADDING_BOTTOM);
        var totalHeight = bubbleHeight + TAIL_HEIGHT;

        using var scope = new PixelBufferScope(bubbleWidth, totalHeight);
        Array.Clear(scope.Pixels, 0, scope.Count);

        ImageUtil.DrawBubbleBody(
            scope.Pixels,
            bubbleWidth,
            0,
            0,
            bubbleWidth,
            bubbleHeight,
            BubbleBorderColor,
            BubbleFillColor);

        ImageUtil.DrawBubbleTail(
            scope.Pixels,
            bubbleWidth,
            bubbleWidth / 2,
            bubbleHeight - 1,
            BubbleBorderColor,
            BubbleFillColor);

        var texture = new Texture2D(ChaosGame.Device, bubbleWidth, totalHeight);
        scope.CommitTo(texture);

        return new ChatBubble(
            entityId,
            texture,
            lines,
            textColor,
            bubbleWidth,
            totalHeight);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || Texture is null)
            return;

        UpdateClipRect();

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        //draw bubble background
        var bgEffect = TailOnTop ? SpriteEffects.FlipVertically : SpriteEffects.None;

        spriteBatch.Draw(
            Texture,
            new Vector2(ScreenX, ScreenY),
            null,
            Color.White,
            0f,
            Vector2.Zero,
            1f,
            bgEffect,
            0f);

        //draw text lines on top of the bubble background
        var textX = ScreenX + LEFT_CAP_WIDTH + INNER_PADDING_LEFT;
        var textY = TailOnTop ? ScreenY + TAIL_HEIGHT + INNER_PADDING_TOP : ScreenY + INNER_PADDING_TOP;

        foreach (var line in Lines)
        {
            DrawTextClipped(
                spriteBatch,
                new Vector2(textX, textY),
                line,
                TextColor);
            textY += LINE_HEIGHT;
        }
    }

    private static string? FindLastColorCode(string line)
    {
        string? last = null;

        for (var i = 0; i < (line.Length - 2); i++)
            if (TextRenderer.IsColorCode(line, i))
            {
                last = line[i..(i + 3)];
                i += 2;
            }

        return last;
    }

    public override void Update(GameTime gameTime) => ElapsedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

    private static List<string> WordWrap(string text)
    {
        var lines = new List<string>();

        if (string.IsNullOrWhiteSpace(text))
        {
            lines.Add(" ");

            return lines;
        }

        var remaining = text.Trim();
        string? activeColorCode = null;

        while (remaining.Length > 0)
        {
            if (remaining.Length <= MAX_LINE_CHARS)
            {
                lines.Add(remaining);

                break;
            }

            var breakAt = MAX_LINE_CHARS;

            for (var i = MAX_LINE_CHARS - 1; i > 0; i--)
                if (remaining[i] == ' ')
                {
                    breakAt = i;

                    break;
                }

            var line = remaining[..breakAt]
                .TrimEnd();

            if (line.Length == 0)
                line = remaining[..MAX_LINE_CHARS];

            lines.Add(line);

            //track the last color code in this line so the next line inherits it
            activeColorCode = FindLastColorCode(line) ?? activeColorCode;

            remaining = remaining[line.Length..]
                .TrimStart();

            //prepend the active color code to the next line so it carries over
            if (activeColorCode is not null && (remaining.Length > 0))
                remaining = activeColorCode + remaining;
        }

        return lines;
    }
}