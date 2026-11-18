#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>The two lists that the Inventory window opens in <see cref="InventoryListModal" />.</summary>
public enum InventoryListKind
{
    Currency,
    QuestItems
}

/// <summary>
///     A row of an Inventory list: the name and the amount. The row keeps a 16 px place for an icon at its left; the icon
///     comes with the entries of the server (HS-1588).
/// </summary>
internal sealed record InventoryListEntry(string Name, uint Amount);

/// <summary>
///     The Currency and Quest Items modal of the Inventory window (BRIG-150, BRIG-127 D33): a themed
///     <see cref="CenteredModalPanel" /> at the centre of the Inventory panel, with ten rows of 20 px that alternate in
///     shade, a scroll bar, and the Close button of the modals. A row shows the name and the amount; a large amount
///     shows in short form (1.2M), and its tooltip shows the full amount. A player cannot trade or move a currency, and
///     a script takes a quest item, so the lists are read-only. It is modal, as the details popups are, so the item
///     tooltip of the window under it does not show. Escape, Close or a press outside the modal closes it.
///     The server sends no currencies (the wallet is HS-1588) and no item tags today, so both lists are empty and the
///     modal says so.
/// </summary>
internal sealed class InventoryListModal : CenteredModalPanel
{
    public const int WIDTH = 230;
    public const int ROW_HEIGHT = 20;
    public const int ROWS = 10;
    private const int TOOLTIP_TEXT_WIDTH = 160;

    private readonly VirtualizedListView<InventoryListEntry, InventoryListRow> List;
    private readonly UILabel EmptyText;
    private readonly TooltipFrame AmountTooltip = new(TOOLTIP_TEXT_WIDTH) { Name = "amount_tooltip" };
    private string? AmountTooltipText;

    public InventoryListModal()
        : base(TitleOf(InventoryListKind.Currency), WIDTH, 0, true)
    {
        Name = "inventory_list";
        IsModal = true;
        SetContentHeight(ROWS * ROW_HEIGHT);

        List = new VirtualizedListView<InventoryListEntry, InventoryListRow>(
            ContentBounds,
            ROW_HEIGHT,
            (width, index) => new InventoryListRow(width, ROW_HEIGHT) { Name = $"row_{index}" },
            (row, slot) => row.Bind(slot))
        {
            Name = "list"
        };

        AddChild(List);

        EmptyText = HudText.Label("empty", ThemeRoles.TextDisabled, HorizontalAlignment.Center);
        EmptyText.SetBounds(ContentBounds);
        AddChild(EmptyText);
    }

    /// <summary>The list that shows.</summary>
    public InventoryListKind Kind { get; private set; }

    /// <summary>The rows of the list that shows.</summary>
    public IReadOnlyList<InventoryListEntry> Entries { get; private set; } = [];

    internal string EmptyShown => EmptyText.Visible ? EmptyText.Text : string.Empty;

    internal IEnumerable<InventoryListRow> Rows => List.Children.OfType<InventoryListRow>();

    /// <summary>The title of a list.</summary>
    public static string TitleOf(InventoryListKind kind) => kind == InventoryListKind.Currency ? "Currencies" : "Quest Items";

    /// <summary>The line of an empty list.</summary>
    public static string EmptyTextOf(InventoryListKind kind) => kind == InventoryListKind.Currency ? "No currencies yet." : "No quest items yet.";

    /// <summary>Shows the list <paramref name="kind" /> with <paramref name="entries" /> at the centre of <paramref name="over" />.</summary>
    /// <param name="over">The screen bounds of the Inventory panel.</param>
    public void Open(InventoryListKind kind, IReadOnlyList<InventoryListEntry> entries, Rectangle over)
    {
        Kind = kind;
        Entries = entries;
        SetTitle(TitleOf(kind));
        List.SetItems(entries);
        EmptyText.Text = EmptyTextOf(kind);
        EmptyText.Visible = entries.Count == 0;
        SetViewportBounds(over);
        Show();
    }

    protected override void LayoutContent()
    {
        var content = ContentBounds;
        List.X = content.X;
        List.Y = content.Y;
        EmptyText.SetBounds(content);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        //the tooltip has no parent: its position is in screen coordinates, and the modal does not clip it
        var hovered = Rows.FirstOrDefault(row => row.Visible && row.ContainsPoint(InputBuffer.MouseX, InputBuffer.MouseY));

        if (hovered?.FullAmount is not { } full)
            return;

        if (full != AmountTooltipText)
        {
            AmountTooltip.SetLines([TooltipLine.Themed(full, ThemeRoles.Text)]);
            AmountTooltipText = full;
        }

        AmountTooltip.PlaceAtCursor(InputBuffer.MouseX, InputBuffer.MouseY);
        AmountTooltip.Visible = true;
        AmountTooltip.Draw(spriteBatch);
    }

    public override void Dispose()
    {
        AmountTooltip.Dispose();

        base.Dispose();
    }
}

/// <summary>
///     A row of <see cref="InventoryListModal" />: the name after the place of the icon, and the amount at the right in
///     short form. Every second row has the <see cref="ThemeRoles.RowAlternate" /> shade.
/// </summary>
internal sealed class InventoryListRow : UIPanel
{
    private const int ICON = 16;
    private const int GAP = 4;
    private const int AMOUNT_WIDTH = 50;

    private readonly UILabel NameText;
    private readonly UILabel AmountText;
    private bool Shaded;

    public InventoryListRow(int width, int height)
    {
        Width = width;
        Height = height;
        Visible = false;

        NameText = HudText.Label("name", ThemeRoles.Text);
        NameText.SetBounds(new Rectangle(GAP + ICON + GAP, 0, width - 3 * GAP - ICON - AMOUNT_WIDTH, height));
        AddChild(NameText);

        AmountText = HudText.Label("amount", ThemeRoles.Text, HorizontalAlignment.Right);
        AmountText.SetBounds(new Rectangle(width - GAP - AMOUNT_WIDTH, 0, AMOUNT_WIDTH, height));
        AddChild(AmountText);
    }

    internal string NameShown => NameText.Text;

    internal string AmountShown => AmountText.Text;

    internal bool IsShaded => Shaded;

    /// <summary>The full amount for the tooltip, when the row shows the short form; else null.</summary>
    public string? FullAmount { get; private set; }

    //the shade is part of the look
    protected internal override Color? DrawnBackgroundColor => Shaded ? ThemeColor(ThemeRoles.RowAlternate) : null;

    /// <summary>Shows the entry of a list slot, or hides the row for an empty slot.</summary>
    public void Bind(VirtualRow<InventoryListEntry> slot)
    {
        if (slot.Kind != VirtualRowKind.Item)
        {
            Visible = false;
            FullAmount = null;

            return;
        }

        var entry = slot.Item;
        Shaded = slot.Index % 2 == 1;
        NameText.Text = entry.Name;
        AmountText.Text = HudFormat.Amount(entry.Amount);
        FullAmount = HudFormat.FullAmount(entry.Amount);
        Visible = true;
    }
}
