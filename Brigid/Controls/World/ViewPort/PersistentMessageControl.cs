#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.ViewPort;

/// <summary>
///     Floating text displayed at the top-right of the viewport. Set by the server and remains until cleared with an empty
///     string.
/// </summary>
public sealed class PersistentMessageControl : UILabel
{
    private const int MAX_CHARS = 25;
    private static readonly int LABEL_WIDTH = MAX_CHARS * TextRenderer.CHAR_WIDTH;

    public PersistentMessageControl(Rectangle viewportBounds)
    {
        Name = "PersistentMessage";
        X = viewportBounds.Right - LABEL_WIDTH - 2;
        Y = viewportBounds.Top + 10;
        Width = LABEL_WIDTH;
        Height = TextRenderer.CHAR_HEIGHT;
        PaddingLeft = 0;
        PaddingRight = 0;
        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;
        ForegroundColor = Color.White;
        IsHitTestVisible = false;
        Visible = false;
    }

    public void SetMessage(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            Text = string.Empty;
            Visible = false;

            return;
        }

        Text = text;
        Visible = true;
    }
}