#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The player plate of the new HUD (BRIG-148): a box in the <see cref="ThemeRoles.HudBox" /> part with the head of the
///     player's aisling at the left (<see cref="Rendering.AislingRenderer.RenderHead" />), and two lines: the name in the
///     title colour, and the level and the class. Before the class arrives (the self profile), the second line shows the
///     level only. A left click raises <see cref="HudBox.Clicked" />.
/// </summary>
internal sealed class HudPlayerPlate : HudBox
{
    private readonly UIImage Icon;
    private readonly UILabel NameText;
    private readonly UILabel DetailText;
    private byte Level;
    private string ClassName = string.Empty;

    public HudPlayerPlate()
        : base("player_plate", MainHudLayout.PlayerPlate)
    {
        Icon = new UIImage
        {
            Name = "icon",
            IsHitTestVisible = false
        };
        Icon.SetBounds(MainHudLayout.PlateIcon);
        AddChild(Icon);

        NameText = AddLabel("name", MainHudLayout.PlateName, HorizontalAlignment.Left, ThemeRoles.TextTitle);
        DetailText = AddLabel("detail", MainHudLayout.PlateDetail, HorizontalAlignment.Left);
    }

    /// <summary>The name of the player.</summary>
    public string PlayerName => NameText.Text;

    /// <summary>The second line: the level and the class.</summary>
    public string Detail => DetailText.Text;

    /// <summary>The head of the aisling, or null while no appearance gave one.</summary>
    public Texture2D? HeadTexture => Icon.Texture;

    public void SetName(string name) => NameText.Text = name;

    public void SetLevel(byte level)
    {
        Level = level;
        ShowDetail();
    }

    public void SetClass(string className)
    {
        ClassName = className.Trim();
        ShowDetail();
    }

    /// <summary>Shows <paramref name="icon" /> as the head of the aisling, and disposes the icon before it.</summary>
    public void SetIcon(Texture2D? icon)
    {
        if (ReferenceEquals(icon, Icon.Texture))
            return;

        Icon.Texture?.Dispose();
        Icon.Texture = icon;
    }

    /// <summary>The second line of the plate: the level, and the class after it when the class is known.</summary>
    internal static string DetailOf(byte level, string className)
        => className.Length == 0 ? HudFormat.Level(level) : $"{HudFormat.Level(level)} {className}";

    private void ShowDetail() => DetailText.Text = DetailOf(Level, ClassName);
}
