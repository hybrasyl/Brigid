#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Chaos.Client.Controls.World.ViewPort;

/// <summary>
///     Floating text pane in the top-left of the viewport. Displays recent system messages (whispers, group/guild chat,
///     active messages) that fade out after a few seconds. No background — just stacked UILabel lines.
/// </summary>
public sealed class SystemMessagePaneControl : UIPanel
{
    private const int MAX_LINES = 3;
    private const float DISPLAY_DURATION_MS = 2500f;
    private const float FADE_DURATION_MS = 1000f;
    private const float TOTAL_DURATION_MS = DISPLAY_DURATION_MS + FADE_DURATION_MS;

    private readonly UILabel[] Lines = new UILabel[MAX_LINES];
    private readonly Color[] BaseColors = new Color[MAX_LINES];
    private int Count;
    private float ElapsedMs;

    public SystemMessagePaneControl(Rectangle viewportBounds)
    {
        Name = "SystemMessagePane";
        Width = TextRenderer.CHAR_WIDTH * 48;
        Height = MAX_LINES * TextRenderer.CHAR_HEIGHT;
        SetViewportBounds(viewportBounds);
        IsHitTestVisible = false;

        for (var i = 0; i < MAX_LINES; i++)
        {
            Lines[i] = new UILabel
            {
                Name = $"SysMsg{i}",
                Width = Width,
                Height = TextRenderer.CHAR_HEIGHT,
                PaddingLeft = 0,
                PaddingRight = 0,
                PaddingTop = 0,
                PaddingBottom = 0,
                ColorCodesEnabled = true,
                ShadowStyle = ShadowStyle.BottomRight,
                Visible = false
            };

            AddChild(Lines[i]);
        }
    }

    public void SetViewportBounds(Rectangle viewportBounds)
    {
        X = viewportBounds.X + 6;
        Y = viewportBounds.Y;
    }

    public void AddMessage(string text, Color? color = null)
    {
        var messageColor = color ?? LegendColors.White;

        //shift existing messages up, dropping oldest
        if (Count >= MAX_LINES)
            for (var i = 0; i < (MAX_LINES - 1); i++)
            {
                Lines[i].Text = Lines[i + 1].Text;
                BaseColors[i] = BaseColors[i + 1];
            }
        else
            Count++;

        //newest at bottom
        var slot = Count - 1;
        Lines[slot].Text = text;
        BaseColors[slot] = messageColor;

        //reset timer and restore all colors/opacity
        ElapsedMs = 0;

        for (var i = 0; i < Count; i++)
        {
            Lines[i].ForegroundColor = BaseColors[i];
            Lines[i].Opacity = 1f;
        }

        RepositionLabels();
    }

    private void RepositionLabels()
    {
        var startY = Height - Count * TextRenderer.CHAR_HEIGHT;

        for (var i = 0; i < MAX_LINES; i++)
            if (i < Count)
            {
                Lines[i].Y = startY + i * TextRenderer.CHAR_HEIGHT;
                Lines[i].X = 0;
                Lines[i].Visible = true;
            } else
                Lines[i].Visible = false;
    }

    public override void Update(GameTime gameTime)
    {
        if (ElapsedMs >= TOTAL_DURATION_MS)
            return;

        ElapsedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        if (ElapsedMs >= TOTAL_DURATION_MS)
            for (var i = 0; i < Count; i++)
                Lines[i].Visible = false;
        else if (ElapsedMs > DISPLAY_DURATION_MS)
        {
            var alpha = 1f - (ElapsedMs - DISPLAY_DURATION_MS) / FADE_DURATION_MS;

            for (var i = 0; i < Count; i++)
                Lines[i].Opacity = alpha;
        }

        base.Update(gameTime);
    }
}