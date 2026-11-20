#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Models;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     A row of the Abilities window (BRIG-149): the icon of the ability, its name, and its level under the name. The icon
///     is a cached texture of <see cref="UiRenderer" />; the row does not own it. On a learned ability a left click raises
///     <see cref="UseRequested" />, a press and a move start a drag with an <see cref="AbilityDragPayload" />, and the clock
///     swipe of the effect bar covers the icon for the part of the cooldown that is left. A right click raises
///     <see cref="DetailsRequested" /> on any ability.
/// </summary>
internal sealed class AbilityRowControl : UIPanel
{
    private readonly Func<AbilityRow, float> CooldownOf;
    private readonly Func<AbilityRow, IconTexture?> IconSource;
    private readonly UILabel NameText;
    private readonly UILabel LevelText;
    private readonly ClockWedgeCache Wedges;
    private IconTexture? Icon;

    /// <param name="iconSource">The icon of a row.</param>
    /// <param name="cooldownOf">The part of the cooldown of a learned ability that is left, from 0 (ready) to 1.</param>
    /// <param name="wedges">The masks of the swipe, at the size of the icon; the window owns them.</param>
    public AbilityRowControl(Func<AbilityRow, IconTexture?> iconSource, Func<AbilityRow, float> cooldownOf, ClockWedgeCache wedges)
    {
        IconSource = iconSource;
        CooldownOf = cooldownOf;
        Wedges = wedges;
        Visible = false;

        NameText = AddLine("name", ThemeRoles.Text);
        LevelText = AddLine("level", ThemeRoles.TextDisabled);
    }

    /// <summary>The ability of the row, or null for an empty row.</summary>
    public AbilityRow? Row { get; private set; }

    internal string NameShown => NameText.Text;

    internal string LevelShown => LevelText.Text;

    internal IconTexture? IconShown => Icon;

    /// <summary>A left click on a learned ability: use it.</summary>
    public event Action<AbilityRow>? UseRequested;

    /// <summary>A right click on an ability: its details.</summary>
    public event Action<AbilityRow>? DetailsRequested;

    public override void OnClick(ClickEvent e)
    {
        if (Row is not { } row)
            return;

        if ((e.Button == MouseButton.Left) && row.IsLearned)
            UseRequested?.Invoke(row);
        else if (e.Button == MouseButton.Right)
            DetailsRequested?.Invoke(row);
        else
            return;

        e.Handled = true;
    }

    //a left drag of a learned ability that is ready, as the classic slots: a drop casts a spell at once
    public override void OnDragStart(DragStartEvent e)
    {
        if ((e.Button == MouseButton.Left) && Row is { Slot: { } slot } row && (CooldownOf(row) <= 0))
            e.Payload = new AbilityDragPayload(row.IsSpell, slot, Icon?.Texture);
    }

    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);
        NameText.SetBounds(AbilitiesLayout.NameIn(bounds.Width));
        LevelText.SetBounds(AbilitiesLayout.LevelIn(bounds.Width));
    }

    /// <summary>
    ///     Shows <paramref name="row" />, or hides the row for null. The icon is read again only for another row, also the
    ///     same ability in another state.
    /// </summary>
    public void Show(AbilityRow? row)
    {
        if (row is null)
        {
            Row = null;
            Icon = null;
            Visible = false;

            return;
        }

        if (row != Row)
            Icon = IconSource(row);

        Row = row;
        NameText.Text = row.Name;
        LevelText.Text = row.Level;
        Visible = true;
    }

    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        base.DrawChrome(spriteBatch, bounds);

        if (Icon is { } icon)
            DrawTexture(spriteBatch, icon.Texture, icon.PositionIn(bounds), Color.White);

        if (Row is { IsLearned: true } row)
            DrawTexture(spriteBatch, Wedges.ForLeft(spriteBatch.GraphicsDevice, CooldownOf(row)), ClockWedgeCache.SwipePosition(bounds, Icon), ClockWedgeCache.SwipeColor);
    }

    private UILabel AddLine(string name, string role)
    {
        var label = HudText.Label(name, role);
        AddChild(label);

        return label;
    }
}
