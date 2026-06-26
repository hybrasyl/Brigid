#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.ViewPort;

/// <summary>
///     Chant text rendered as plain blue text above an entity's head. No bubble, no name prefix. Max 32 characters, 18
///     chars per visual line with character wrap (not word wrap). If total character count is 10 or less, text is centered
///     per line; otherwise left-aligned.
/// </summary>
public sealed class ChantText : UIPanel
{
    private const int CHARS_PER_LINE = 18;
    private const int LINE_HEIGHT = 12;
    private const int MAX_CHARS = 32;
    private const int CENTER_THRESHOLD = 10;
    private const float DISPLAY_DURATION_MS = 3000f;

    private static readonly Color ChantColor = new(100, 149, 237);

    private float ElapsedMs;

    public uint EntityId { get; }
    public bool IsExpired => ElapsedMs >= DISPLAY_DURATION_MS;

    private ChantText(uint entityId, int width, int height)
    {
        EntityId = entityId;
        Width = width;
        Height = height;
    }

    public static ChantText Create(uint entityId, string message)
    {
        var text = message.Length > MAX_CHARS ? message[..MAX_CHARS] : message;
        var centered = text.Length <= CENTER_THRESHOLD;

        //character-wrap into visual lines
        var visualLines = new List<string>();
        var remaining = text;

        while (remaining.Length > 0)
        {
            if (remaining.Length <= CHARS_PER_LINE)
            {
                visualLines.Add(remaining);

                break;
            }

            visualLines.Add(remaining[..CHARS_PER_LINE]);
            remaining = remaining[CHARS_PER_LINE..];
        }

        if (visualLines.Count == 0)
            visualLines.Add(" ");

        var textAreaWidth = CHARS_PER_LINE * 6 + 2;
        var totalHeight = visualLines.Count * LINE_HEIGHT;

        var overlay = new ChantText(entityId, textAreaWidth, totalHeight);

        for (var i = 0; i < visualLines.Count; i++)
        {
            var line = visualLines[i];
            var lineWidth = TextRenderer.MeasureWidth(line);

            var label = new UILabel
            {
                Name = $"Line{i}",
                X = centered ? (textAreaWidth - lineWidth) / 2 : 0,
                Y = i * LINE_HEIGHT,
                Width = lineWidth,
                Height = LINE_HEIGHT,
                Text = line,
                ForegroundColor = ChantColor,
                PaddingLeft = 0,
                PaddingRight = 0,
                PaddingTop = 0
            };

            overlay.AddChild(label);
        }

        return overlay;
    }

    public override void Update(GameTime gameTime) => ElapsedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
}