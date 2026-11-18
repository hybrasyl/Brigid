#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.Models;
using Brigid.Rendering;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     The Legend window of the new HUD (BRIG-151): the legend marks of <see cref="SelfProfile" /> in a list at the full
///     size of the content, one row for each mark, with the icon and then the text in the colour of the mark, in the layout
///     of the legacy Legend tab. The player sees their own invisible marks: such a row shows the text in the disabled text
///     colour (<see cref="LegendMarkEntry.IsHidden" />). A row gets its icon when it binds, so a pack icon of any id
///     shows. A new list of marks shows from the top; a reply with the same marks keeps the scroll. The book in the title
///     row of the Details window opens it.
/// </summary>
internal sealed class LegendWindowControl : HudWindowContent
{
    public const int ROW_HEIGHT = 20;
    public const string EMPTY_TEXT = "No legend marks.";

    private readonly SelfProfile Profile;
    private readonly Func<byte, Texture2D?> IconOf;
    private readonly UILabel EmptyText;

    private VirtualizedListView<LegendMarkEntry, LegendMarkRow>? List;
    private IReadOnlyList<LegendMarkEntry>? ReadMarks;

    /// <param name="profile">The player's own profile.</param>
    /// <param name="iconOf">The icon of a mark id, or null when no art has it; the default reads the UI renderer.</param>
    public LegendWindowControl(SelfProfile profile, Func<byte, Texture2D?>? iconOf = null)
    {
        Name = "legend_window";
        Profile = profile;
        IconOf = iconOf ?? (id => UiRenderer.Instance?.GetLegendMarkIcon(id));

        EmptyText = HudText.Label("empty", ThemeRoles.TextDisabled, HorizontalAlignment.Center);
        EmptyText.Text = EMPTY_TEXT;
        AddChild(EmptyText);

        Profile.Changed += ShowMarks;
        ShowMarks();
    }

    /// <summary>The marks that have a row, in the order of the profile.</summary>
    public IReadOnlyList<LegendMarkEntry> Marks { get; private set; } = [];

    internal bool EmptyShown => EmptyText.Visible;

    internal IEnumerable<LegendMarkRow> Rows => List?.Children.OfType<LegendMarkRow>() ?? [];

    internal VirtualizedListView<LegendMarkEntry, LegendMarkRow>? ListView => List;

    /// <summary>Binds a row as the list does when it draws: the mark and its icon, or no row for an empty slot.</summary>
    internal void BindRow(LegendMarkRow row, VirtualRow<LegendMarkEntry> slot) => row.Bind(slot, IconOf);

    //the profile changes for its name, portrait and text too, and each self profile reply (a group action asks for one)
    //gives a new list; only other marks change the rows and the scroll. Before the first profile, the window does not say
    //that there are no marks
    private void ShowMarks()
    {
        EmptyText.Visible = Profile.HasProfile && (Marks.Count == 0);

        if (ReferenceEquals(Profile.LegendMarks, ReadMarks))
            return;

        ReadMarks = Profile.LegendMarks;
        var marks = Profile.LegendMarks.ToList();

        if (marks.SequenceEqual(Marks))
            return;

        Marks = marks;
        List?.SetItems(Marks);
        EmptyText.Visible = Profile.HasProfile && (Marks.Count == 0);
    }

    public override void Dispose()
    {
        Profile.Changed -= ShowMarks;

        base.Dispose();
    }

    //the list keeps a pool of rows for its height, so a new size makes a new list
    protected override void LayOut(int width, int height)
    {
        EmptyText.SetBounds(new Rectangle(0, 0, width, height));

        if ((List is not null) && (List.Width == width) && (List.Height == height))
            return;

        if (List is not null)
            RemoveChild(List.Name);

        List = new VirtualizedListView<LegendMarkEntry, LegendMarkRow>(
            new Rectangle(0, 0, width, height),
            ROW_HEIGHT,
            (rowWidth, index) => new LegendMarkRow(rowWidth, ROW_HEIGHT) { Name = $"row_{index}" },
            BindRow)
        {
            Name = "list"
        };

        List.SetItems(Marks);
        AddChild(List);
    }
}

/// <summary>
///     A row of the Legend window: the icon of the mark, then its text in the colour of the mark, or in the disabled text
///     colour for an invisible mark.
/// </summary>
internal sealed class LegendMarkRow : UIPanel
{
    //the size of a frame of legends.epf; a pack icon of another size scales to fit it
    public const int ICON_WIDTH = 21;
    public const int ICON_HEIGHT = 20;
    private const int GAP = 5;

    private readonly UIImage Icon;
    private readonly UILabel Text;

    public LegendMarkRow(int width, int height)
    {
        Width = width;
        Height = height;
        IsHitTestVisible = false;

        Icon = new UIImage
        {
            Name = "icon",
            ScaleToFit = true,
            IsHitTestVisible = false
        };

        Icon.SetBounds(new Rectangle(0, (height - ICON_HEIGHT) / 2, ICON_WIDTH, ICON_HEIGHT));
        AddChild(Icon);

        //the colour of a mark carries meaning, so the text takes it and not a theme colour; only an invisible mark takes the
        //disabled text colour
        Text = new UILabel
        {
            Name = "text",
            VerticalAlignment = VerticalAlignment.Center,
            PaddingLeft = 0,
            PaddingRight = 0,
            IsHitTestVisible = false
        };

        Text.SetBounds(new Rectangle(ICON_WIDTH + GAP, 0, width - ICON_WIDTH - GAP, height));
        AddChild(Text);
    }

    internal string TextShown => Text.Text;

    internal Color ColorShown => Text.ForegroundColor;

    internal Texture2D? IconShown => Icon.Texture;

    /// <summary>Shows the mark of a list slot with the icon that <paramref name="iconOf" /> gives, or hides the row for an empty slot.</summary>
    public void Bind(VirtualRow<LegendMarkEntry> slot, Func<byte, Texture2D?> iconOf)
    {
        if (slot.Kind != VirtualRowKind.Item)
        {
            Visible = false;
            Icon.Texture = null;
            Text.Text = string.Empty;

            return;
        }

        var mark = slot.Item;
        Icon.Texture = iconOf(mark.Icon);
        Text.Text = mark.Text;
        Text.ForegroundColor = mark.IsHidden ? ThemeColor(ThemeRoles.TextDisabled) : mark.Color;
        Visible = true;
    }
}
