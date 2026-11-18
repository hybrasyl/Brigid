#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main.Windows;

/// <summary>
///     A row of the Abilities window (BRIG-149): the icon of the ability, its name, and its level under the name. The icon
///     is a cached texture of <see cref="UiRenderer" />; the row does not own it.
/// </summary>
internal sealed class AbilityRowControl : UIPanel
{
    private readonly Func<AbilityRow, IconTexture?> IconSource;
    private readonly UILabel NameText;
    private readonly UILabel LevelText;
    private IconTexture? Icon;

    public AbilityRowControl(Func<AbilityRow, IconTexture?> iconSource)
    {
        IconSource = iconSource;
        Visible = false;

        NameText = AddLine("name", ThemeRoles.Text);
        LevelText = AddLine("level", ThemeRoles.TextDisabled);
    }

    /// <summary>The ability of the row, or null for an empty row.</summary>
    public AbilityRow? Row { get; private set; }

    internal string NameShown => NameText.Text;

    internal string LevelShown => LevelText.Text;

    internal IconTexture? IconShown => Icon;

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
            DrawTexture(spriteBatch, icon.Texture, new Vector2(bounds.X + icon.OffsetX, bounds.Y + icon.OffsetY), Color.White);
    }

    private UILabel AddLine(string name, string role)
    {
        var label = new UILabel
        {
            Name = name,
            VerticalAlignment = VerticalAlignment.Center,
            PaddingLeft = 0,
            PaddingRight = 0,
            IsHitTestVisible = false
        };

        label.SetForegroundRole(role);
        AddChild(label);

        return label;
    }
}
