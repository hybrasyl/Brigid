#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     Personal View of the Equipment window (BRIG-151, D31): the read-only stats of the player, one row each, in the order
///     of <see cref="PersonalStats.Order" />. A double click or a right click on a row toggles its pin (Sabrael,
///     2026-11-18, Q6); a pinned row shows an asterisk. The window keeps the pins and saves them through
///     <see cref="PinsChanged" />.
/// </summary>
internal sealed class PersonalViewControl : UIPanel
{
    public const int ROW_HEIGHT = 16;

    private readonly PlayerAttributes Attributes;
    private readonly PersonalViewRow[] Rows;

    public PersonalViewControl(PlayerAttributes attributes)
    {
        Name = "personal_view";
        Attributes = attributes;
        Rows = new PersonalViewRow[PersonalStats.All.Length];

        for (var i = 0; i < Rows.Length; i++)
        {
            var row = new PersonalViewRow(i);
            row.Toggled += OnToggled;
            Rows[i] = row;
            AddChild(row);
        }

        ShowRows();
        Attributes.Changed += ShowValues;
    }

    /// <summary>The pinned stats, in the order of pinning.</summary>
    public IReadOnlyList<PersonalStat> Pins { get; private set; } = [];

    /// <summary>A row was pinned or unpinned: the world screen saves the pins for the character.</summary>
    public event Action<IReadOnlyList<PersonalStat>>? PinsChanged;

    internal IReadOnlyList<PersonalViewRow> RowControls => Rows;

    /// <summary>Shows the saved pins of the character, with no <see cref="PinsChanged" />.</summary>
    public void SetPins(IReadOnlyList<PersonalStat> pins)
    {
        Pins = pins.Distinct().ToArray();
        ShowRows();
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);

        for (var i = 0; i < Rows.Length; i++)
            Rows[i].SetBounds(new Rectangle(0, i * ROW_HEIGHT, bounds.Width, ROW_HEIGHT));
    }

    public override void Dispose()
    {
        Attributes.Changed -= ShowValues;

        base.Dispose();
    }

    private void OnToggled(PersonalStat stat)
    {
        Pins = PersonalStats.Toggle(Pins, stat);
        ShowRows();
        PinsChanged?.Invoke(Pins);
    }

    private void ShowRows()
    {
        var order = PersonalStats.Order(Pins);

        for (var i = 0; i < Rows.Length; i++)
            Rows[i].Bind(order[i], Pins.Contains(order[i]));

        ShowValues();
    }

    private void ShowValues()
    {
        foreach (var row in Rows)
            row.ShowValue(PersonalStats.ValueOf(row.Stat, Attributes));
    }
}

/// <summary>A row of Personal View: the label, with an asterisk when it is pinned, and the value at the right.</summary>
internal sealed class PersonalViewRow : UIPanel
{
    private const int PAD = 3;

    //the longest value today is an element name or a large number
    private const int VALUE_WIDTH = 42;

    private readonly UILabel LabelText;
    private readonly UILabel ValueText;
    private readonly int Index;

    public PersonalViewRow(int index)
    {
        Index = index;
        Name = $"row_{index}";

        LabelText = HudText.Label("label", ThemeRoles.Text);
        AddChild(LabelText);
        ValueText = HudText.Label("value", ThemeRoles.Text, HorizontalAlignment.Right);
        AddChild(ValueText);
    }

    public PersonalStat Stat { get; private set; }

    public bool Pinned { get; private set; }

    internal string LabelShown => LabelText.Text;

    internal string ValueShown => ValueText.Text;

    /// <summary>A double click or a right click on the row: toggle the pin of its stat.</summary>
    public event Action<PersonalStat>? Toggled;

    public void Bind(PersonalStat stat, bool pinned)
    {
        Stat = stat;
        Pinned = pinned;
        LabelText.Text = pinned ? $"* {PersonalStats.LabelOf(stat)}" : PersonalStats.LabelOf(stat);
    }

    public void ShowValue(string value) => ValueText.Text = value;

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        var valueLeft = Math.Max(PAD, bounds.Width - PAD - VALUE_WIDTH);
        LabelText.SetBounds(new Rectangle(PAD, 0, valueLeft - PAD, bounds.Height));
        ValueText.SetBounds(new Rectangle(valueLeft, 0, bounds.Width - PAD - valueLeft, bounds.Height));
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button == MouseButton.Right)
            Toggled?.Invoke(Stat);

        e.Handled = true;
    }

    public override void OnDoubleClick(DoubleClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        Toggled?.Invoke(Stat);
        e.Handled = true;
    }

    //every second row is shaded, as the lists of the Inventory window
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (Index % 2 == 1)
            DrawRectClipped(spriteBatch, bounds, ThemeColor(ThemeRoles.RowAlternate));
    }
}
