#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     A meter of the bottom area of the new HUD (BRIG-148): the health, mana or experience bar. It has a track, a fill to
///     the fraction of a value, and an optional label at the left and value at the right. A themed meter draws the part of
///     its track role at the full meter, and the part of its fill role at the full meter clipped at the fraction, so the fill
///     art does not stretch. With no part, the flat look: the <see cref="DialogPalette.MeterTrack" /> fill with a border in
///     the theme's divider colour, and the flat fill colour inside the border. A flat fill shows the gaps between its
///     segments over the full meter; a theme that gives a fill part gives its segments in its art. A meter does not take the
///     mouse, unless its owner sets <see cref="UIElement.IsHitTestVisible" /> for a tooltip.
/// </summary>
internal sealed class HudMeter : UIPanel
{
    private readonly UILabel LabelText;
    private readonly UILabel ValueText;

    public HudMeter(string name, Rectangle bounds, string trackRole, string fillRole, Color flatFill)
    {
        Name = name;
        TrackRole = trackRole;
        FillRole = fillRole;
        FlatFill = flatFill;
        SetBounds(bounds);
        BackgroundColor = DialogPalette.MeterTrack;
        BorderColor = DialogPalette.Divider;
        IsHitTestVisible = false;

        LabelText = AddText("label", HorizontalAlignment.Left);
        ValueText = AddText("value", HorizontalAlignment.Right);
    }

    /// <summary>The theme part of the empty meter.</summary>
    public string TrackRole { get; }

    /// <summary>The theme part of the fill.</summary>
    public string FillRole { get; set; }

    /// <summary>The colour of the fill in the flat look.</summary>
    public Color FlatFill { get; set; }

    /// <summary>The number of segments; 1 or less shows no gaps.</summary>
    public int Segments { get; init; } = 1;

    /// <summary>The filled part of the meter, from 0 to 1.</summary>
    public float Fraction { get; private set; }

    /// <summary>The text at the left of the meter.</summary>
    public string Label
    {
        get => LabelText.Text;
        set => LabelText.Text = value;
    }

    /// <summary>The text at the right of the meter.</summary>
    public string Value
    {
        get => ValueText.Text;
        set => ValueText.Text = value;
    }

    /// <summary>The mouse came over the meter, while it takes the mouse.</summary>
    public event Action<HudMeter>? Entered;

    /// <summary>The mouse left the meter.</summary>
    public event Action? Left;

    //the flat border is part of the look
    protected internal override Color? DrawnBorderColor => ThemeColor(ThemeRoles.Divider);

    public override void OnMouseEnter() => Entered?.Invoke(this);

    public override void OnMouseLeave() => Left?.Invoke();

    /// <summary>Sets the fill to <paramref name="current" /> of <paramref name="max" />; a maximum of 0 gives an empty meter.</summary>
    public void SetFill(uint current, uint max) => Fraction = FractionOf(current, max);

    /// <summary>Sets the fill to <paramref name="fraction" />, clamped to 0 to 1.</summary>
    public void SetFill(float fraction) => Fraction = float.IsNaN(fraction) ? 0 : Math.Clamp(fraction, 0f, 1f);

    /// <summary><paramref name="current" /> of <paramref name="max" />, clamped to 0 to 1; 0 for a maximum of 0.</summary>
    internal static float FractionOf(uint current, uint max) => max == 0 ? 0 : Math.Min(1f, (float)((double)current / max));

    /// <summary>
    ///     The filled part of <paramref name="area" /> at <paramref name="fraction" />, from the left. A fraction between 0 and
    ///     1 fills 1 px at least and leaves 1 px at least, so a low value does not look empty and a high value does not look
    ///     full.
    /// </summary>
    internal static Rectangle FillIn(Rectangle area, float fraction)
    {
        fraction = Math.Clamp(fraction, 0f, 1f);
        var width = (int)MathF.Round(area.Width * fraction);

        if ((fraction > 0) && (fraction < 1) && (area.Width > 1))
            width = Math.Clamp(width, 1, area.Width - 1);

        return area with { Width = width };
    }

    /// <summary>The area of the flat fill: the meter inside its 1 px border.</summary>
    internal static Rectangle FlatFillArea(Rectangle bounds) => new(bounds.X + 1, bounds.Y + 1, bounds.Width - 2, bounds.Height - 2);

    /// <summary>
    ///     The x of each gap between <paramref name="segments" /> segments of <paramref name="area" />, a 1 px line at the end
    ///     of each segment but the last. The segments share the width, and the gaps are at its even fractions.
    /// </summary>
    internal static IEnumerable<int> GapsIn(Rectangle area, int segments)
    {
        for (var i = 1; i < segments; i++)
            yield return area.X + (int)MathF.Round((float)area.Width * i / segments);
    }

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        if (!TryDrawThemePart(spriteBatch, TrackRole, bounds))
            DrawFlatChrome(spriteBatch, bounds);

        if (TryDrawThemePart(spriteBatch, FillRole, bounds, shown: FillIn(bounds, Fraction)))
            return;

        var area = FlatFillArea(bounds);
        var fill = FillIn(area, Fraction);

        if (fill.Width > 0)
            DrawRectClipped(spriteBatch, fill, FlatFill);

        foreach (var x in GapsIn(area, Segments))
            DrawRectClipped(spriteBatch, new Rectangle(x, area.Y, 1, area.Height), DrawnBorderColor!.Value);
    }

    private UILabel AddText(string name, HorizontalAlignment alignment)
    {
        var label = new UILabel
        {
            Name = name,
            HorizontalAlignment = alignment,
            VerticalAlignment = VerticalAlignment.Center,
            PaddingLeft = 4,
            PaddingRight = 4,
            IsHitTestVisible = false
        };

        label.SetBounds(new Rectangle(0, 0, Width, Height));
        label.SetForegroundRole(ThemeRoles.Text);
        AddChild(label);

        return label;
    }
}
