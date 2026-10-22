#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.ViewPort;

/// <summary>
///     The group box over a recruiting aisling (BRIG-126): the group title, centred in a small frame. The frame is
///     the theme's <see cref="ThemeRoles.FramePopup" /> part, else the flat frame; it replaces the legacy 103 by 19
///     <c>gc_pane2.spf</c> pane, and the title area keeps the legacy size. The title takes the
///     <see cref="ThemeRoles.Text" /> role. While the mouse is on the box, the title area takes the
///     <see cref="ThemeRoles.RowHover" /> fill. A click on the box shows the group box viewer.
///     <para>
///         The box is not in the control tree: <c>EntityOverlayManager</c> places it, draws it and hit-tests it (the click
///         and the hover). A themed frame with a wide border makes the box larger, so the manager reads
///         <see cref="UIElement.Width" /> and <see cref="UIElement.Height" /> after <see cref="UpdateText" />, which sizes
///         the box for the current theme.
///     </para>
/// </summary>
public sealed class GroupBox : FramePanel
{
    /// <summary>The width of the title area, as the legacy title rect (3,3)-(100,16).</summary>
    internal const int TITLE_WIDTH = 97;

    /// <summary>The height of the title area.</summary>
    internal const int TITLE_HEIGHT = 13;

    /// <summary>The least space between the edge and the title; <see cref="FramePanel" /> adds the border and a gap.</summary>
    internal const int PADDING = 3;

    private readonly UILabel Label;

    public bool IsHovered { get; set; }

    public uint EntityId { get; }

    /// <summary>The title that the box shows. Internal for the tests.</summary>
    internal string Text => Label.Text;

    /// <summary>The area in the frame: the title, and the hover fill. Internal for the tests.</summary>
    internal Rectangle TextBounds => new(Label.X, Label.Y, Label.Width, Label.Height);

    public GroupBox(uint entityId)
    {
        EntityId = entityId;
        UseTheme = true;

        Label = new UILabel
        {
            Name = "title",
            PaddingLeft = 1,
            PaddingTop = 1,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        Label.SetForegroundRole(ThemeRoles.Text);
        AddChild(Label);

        Fit();
    }

    protected override string FrameRole => ThemeRoles.FramePopup;

    /// <summary>Sets the title, and sizes the box for the current theme.</summary>
    public void UpdateText(string text)
    {
        Label.Text = text;
        Fit();
    }

    //the title area keeps the legacy size, and the frame goes around it: 105 by 21 with the flat frame
    private void Fit()
    {
        var inset = InsetFor(PADDING);

        Width = TITLE_WIDTH + 2 * inset;
        Height = TITLE_HEIGHT + 2 * inset;
        Label.X = inset;
        Label.Y = inset;
        Label.Width = TITLE_WIDTH;
        Label.Height = TITLE_HEIGHT;
    }

    //the hover fill draws over the frame and under the title
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        base.DrawChrome(spriteBatch, bounds);

        if (IsHovered)
            DrawRectClipped(
                spriteBatch,
                new Rectangle(bounds.X + Label.X, bounds.Y + Label.Y, Label.Width, Label.Height),
                ThemeColor(ThemeRoles.RowHover));
    }
}
