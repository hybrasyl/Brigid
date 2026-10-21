#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups.Exchange;

/// <summary>
///     A single item row in the exchange panel. Displays a 32x32 icon on the left and the item name (with count for
///     stackables) vertically centered beside it. The icon is content: the item pack's art, else the legacy item sprite.
///     The name takes the <c>text</c> theme role.
/// </summary>
public sealed class ExchangeItemControl : UIPanel
{
    private const int ICON_SIZE = 32;
    private const int TEXT_OFFSET_X = 36;
    private int BaseX;
    private readonly UIImage Icon;

    private readonly UILabel NameLabel;

    /// <summary>
    ///     Horizontal pixel offset applied to the entire entry. Set by ExchangeControl when the horizontal scrollbar moves.
    /// </summary>
    public int HorizontalOffset
    {
        get;
        set
        {
            field = value;
            X = BaseX - value;
        }
    }

    /// <summary>
    ///     Full pixel width of this entry (icon + padding + text). Used to compute horizontal scrollbar MaxValue.
    /// </summary>
    public int EntryWidth { get; private set; }

    /// <summary>
    ///     The width of the row with no item, and the least width with one. The row grows to the width of its name, so that
    ///     the horizontal bar can show the end of a long name; the list clips the row.
    /// </summary>
    public int MinWidth
    {
        get;
        init
        {
            field = value;
            Width = value;
        }
    } = TEXT_OFFSET_X + 200;

    /// <summary>The item name that the row shows. Internal for the tests.</summary>
    internal string ItemName => NameLabel.Text;

    public ExchangeItemControl()
    {
        Width = MinWidth;
        Height = ICON_SIZE;
        Visible = false;
        IsHitTestVisible = false;

        Icon = new UIImage
        {
            Name = "icon",
            Width = ICON_SIZE,
            Height = ICON_SIZE
        };

        AddChild(Icon);

        NameLabel = new UILabel
        {
            Name = "name",
            X = TEXT_OFFSET_X,
            Y = (Height - TextRenderer.CHAR_HEIGHT) / 2,
            Width = 200,
            Height = TextRenderer.CHAR_HEIGHT,
            PaddingLeft = 0,
            PaddingRight = 0
        };
        NameLabel.SetForegroundRole(ThemeRoles.Text);

        AddChild(NameLabel);
    }

    /// <summary>
    ///     Sets the base X position used as the anchor before horizontal offset is applied.
    /// </summary>
    public void SetBaseX(int x)
    {
        BaseX = x;
        X = x - HorizontalOffset;
    }

    public void ClearItem()
    {
        Icon.Texture = null;
        EntryWidth = 0;
        Width = MinWidth;
        Visible = false;
    }

    public void SetItem(ushort sprite, DisplayColor color, string name)
    {
        //the renderer is absent in the tests; the row then shows the name with no icon
        if (UiRenderer.Instance?.GetItemIcon(sprite, color) is { } icon)
        {
            Icon.Texture = icon.Texture;
            Icon.TextureOffset = icon.Offset;
        } else
            Icon.Texture = null;

        var textWidth = TextRenderer.MeasureWidth(name);
        NameLabel.Width = textWidth;
        NameLabel.Text = name;
        EntryWidth = TEXT_OFFSET_X + textWidth;
        Width = Math.Max(MinWidth, EntryWidth);
        Visible = true;
    }
}
