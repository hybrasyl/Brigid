#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     Base for the status book's detail popups (BRIG-122): <see cref="AbilityMetadataDetailsControl" /> and
///     <see cref="EventMetadataDetailsControl" />. A themed, modal <see cref="CenteredModalPanel" /> with the entry's name as
///     its title, an icon at the top left, rows of text beside and below the icon, and a wrapped description.
///     <para>
///         The height fits the description, from <see cref="MIN_DESCRIPTION_LINES" /> to
///         <see cref="MAX_DESCRIPTION_LINES" /> lines; the mouse wheel scrolls a longer one. A click on the popup, Escape and
///         Enter close it, and the world screen closes it on a click outside it. The icon is game content, so it keeps its
///         art.
///     </para>
/// </summary>
public abstract class StatusBookDetailsPanel : CenteredModalPanel
{
    protected const int ICON_SIZE = 32;
    protected const int ROW_H = 14;
    protected const int GAP = 6;
    protected const int MIN_DESCRIPTION_LINES = 3;
    protected const int MAX_DESCRIPTION_LINES = 14;
    private const int PANEL_W = 300;
    private const int ICON_TO_TEXT = 8;
    private const int CAPTION_GAP = 4;

    private readonly UILabel DescriptionLabel;

    //the height of the icon or of the rows beside it, whichever is taller
    private readonly int HeaderHeight;
    private readonly int BodyHeight;

    //the description lines that the panel was last fitted to, and the FontEngine generation it was fitted at
    private int DescriptionLines = MIN_DESCRIPTION_LINES;
    private readonly FontGenerationStamp Fitted = new();

    protected UIImage Icon { get; }

    /// <param name="name">The panel name, <c>[a-z0-9_]</c>.</param>
    /// <param name="headerRows">The number of rows beside the icon.</param>
    /// <param name="bodyHeight">The height of the content between the icon and the description.</param>
    protected StatusBookDetailsPanel(string name, int headerRows, int bodyHeight)
        //FitDescription sets the height
        : base(string.Empty, PANEL_W, 0, useTheme: true)
    {
        Name = name;
        IsModal = true;

        Icon = new UIImage
        {
            Name = "icon",
            Width = ICON_SIZE,
            Height = ICON_SIZE,
            IsHitTestVisible = false
        };
        AddChild(Icon);

        DescriptionLabel = new UILabel
        {
            Name = "description",
            WordWrap = true,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        DescriptionLabel.SetForegroundRole(ThemeRoles.Text);
        AddChild(DescriptionLabel);

        HeaderHeight = Math.Max(ICON_SIZE, headerRows * ROW_H);
        BodyHeight = bodyHeight;
        SetContentHeight(ContentHeightFor(MIN_DESCRIPTION_LINES));
    }

    /// <summary>The left edge of the rows beside the icon, in panel coordinates.</summary>
    protected int HeaderX => ContentBounds.X + ICON_SIZE + ICON_TO_TEXT;

    /// <summary>The width of the rows beside the icon.</summary>
    protected int HeaderWidth => ContentBounds.Right - HeaderX;

    /// <summary>The top of the rows beside the icon.</summary>
    protected int HeaderY => ContentBounds.Y + GAP;

    /// <summary>The top of the content between the icon and the description.</summary>
    protected int BodyY => HeaderY + HeaderHeight + GAP;

    //the content height for a description of the given number of lines
    private int ContentHeightFor(int lines) => GAP + HeaderHeight + GAP + BodyHeight + lines * TextRenderer.CHAR_HEIGHT + GAP;

    /// <summary>
    ///     Places the icon and the description. A subclass places its own rows, from <see cref="HeaderX" />,
    ///     <see cref="HeaderY" /> and <see cref="BodyY" />, and calls this.
    /// </summary>
    protected override void LayoutContent()
    {
        var content = ContentBounds;

        Icon.X = content.X;
        Icon.Y = HeaderY;

        DescriptionLabel.X = content.X;
        DescriptionLabel.Y = BodyY + BodyHeight;
        DescriptionLabel.Width = content.Width;
        DescriptionLabel.Height = DescriptionLines * TextRenderer.CHAR_HEIGHT;
    }

    /// <summary>
    ///     Styles a row of text that the caller made, in the theme's text colour, and adds it. A caller that colours it for a
    ///     meaning sets the colour after.
    /// </summary>
    protected UILabel AddRow(UILabel label)
    {
        label.Height = ROW_H;
        label.PaddingLeft = 0;
        label.PaddingRight = 0;
        label.VerticalAlignment = VerticalAlignment.Center;
        label.IsHitTestVisible = false;
        label.SetForegroundRole(ThemeRoles.Text);
        AddChild(label);

        return label;
    }

    /// <summary>Styles a caption that the caller made, in the theme's less prominent text colour, and adds it.</summary>
    protected UILabel AddCaption(UILabel label)
    {
        AddRow(label);
        label.SetForegroundRole(ThemeRoles.TextDisabled);

        return label;
    }

    /// <summary>Places a row at <paramref name="x" />, <paramref name="y" />, <paramref name="width" /> px wide.</summary>
    protected static void PlaceRow(UILabel label, int x, int y, int width)
    {
        label.X = x;
        label.Y = y;
        label.Width = width;
    }

    /// <summary>The width of the widest caption, and a gap, at the current face.</summary>
    protected static int CaptionWidth(params UILabel[] captions)
        => captions.Max(caption => TextRenderer.MeasureWidth(caption.Text)) + CAPTION_GAP;

    /// <summary>Shows the popup for an entry, centered in <paramref name="viewport" />.</summary>
    protected void ShowEntry(string title, string description, Rectangle viewport)
    {
        SetTitle(title);
        DescriptionLabel.Text = description;
        DescriptionLabel.ScrollOffset = 0;
        SetViewportBounds(viewport);
        FitDescription();
        Show();
    }

    //sizes the panel to the wrapped description, and places the content again
    private void FitDescription()
    {
        DescriptionLabel.Width = ContentBounds.Width;

        //a new width or a new FontEngine generation wraps the text again
        DescriptionLabel.Text = DescriptionLabel.Text;
        Fitted.Mark();

        DescriptionLines = Math.Clamp(DescriptionLabel.ContentHeight / TextRenderer.CHAR_HEIGHT, MIN_DESCRIPTION_LINES, MAX_DESCRIPTION_LINES);
        SetContentHeight(ContentHeightFor(DescriptionLines));
        LayoutContent();
    }

    public override void Update(GameTime gameTime)
    {
        //a new face or window size wraps the description again, and changes the caption widths
        if (Visible && Fitted.Changed)
            FitDescription();

        base.Update(gameTime);
    }

    //the wheel scrolls a description that is longer than the panel shows
    public override void OnMouseScroll(MouseScrollEvent e)
    {
        var hidden = Math.Max(0, DescriptionLabel.ContentHeight - DescriptionLabel.Height);

        DescriptionLabel.ScrollOffset = Math.Clamp(
            DescriptionLabel.ScrollOffset - e.Delta * TextRenderer.CHAR_HEIGHT,
            0,
            hidden);

        e.Handled = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        //Alt+Enter changes the window size; it must not close the popup
        if ((e.Key == Keys.Enter) && !e.Alt)
        {
            Hide();
            e.Handled = true;

            return;
        }

        //Escape closes the popup, and the base takes every other key
        base.OnKeyDown(e);
    }

    //a click anywhere on the popup closes it, as before
    public override void OnClick(ClickEvent e)
    {
        Hide();
        e.Handled = true;
    }
}
