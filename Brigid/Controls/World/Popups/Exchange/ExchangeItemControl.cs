#region
using Brigid.Controls.Components;
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.Controls.World.Popups.Exchange;

/// <summary>
///     A single item row in the exchange panel. Displays a 32x32 icon on the left and the item name (with count for
///     stackables) vertically centered beside it.
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

    public ExchangeItemControl()
    {
        Width = TEXT_OFFSET_X + 200;
        Height = ICON_SIZE;
        Visible = false;

        Icon = new UIImage
        {
            Name = "Icon",
            Width = ICON_SIZE,
            Height = ICON_SIZE
        };

        AddChild(Icon);

        NameLabel = new UILabel
        {
            Name = "ItemName",
            X = TEXT_OFFSET_X,
            Y = (Height - TextRenderer.CHAR_HEIGHT) / 2,
            Width = 200,
            Height = TextRenderer.CHAR_HEIGHT,
            PaddingLeft = 0,
            PaddingRight = 0
        };

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
        Visible = false;
    }

    public void SetItem(ushort sprite, DisplayColor color, string name)
    {
        Icon.Texture = UiRenderer.Instance!.GetItemIcon(sprite, color);
        var textWidth = TextRenderer.MeasureWidth(name);
        NameLabel.Width = textWidth;
        NameLabel.Text = name;
        EntryWidth = TEXT_OFFSET_X + textWidth;
        Visible = true;
    }
}