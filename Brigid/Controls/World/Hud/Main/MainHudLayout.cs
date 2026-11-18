#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     Every rectangle of the new HUD, in virtual px on the 640 × 480 screen (BRIG-127 D28, D36, D37; the HUD layer of
///     the HUD mockup in <c>loures</c>). The HUD, its panel host and the tests read the positions here, and no other file
///     holds one.
/// </summary>
internal static class MainHudLayout
{
    /// <summary>The distance from the world edge to a panel, and between the two compact panels.</summary>
    public const int PANEL_INSET = 4;

    private const int SCREEN_WIDTH = ChaosGame.VIRTUAL_WIDTH;
    private const int SCREEN_HEIGHT = ChaosGame.VIRTUAL_HEIGHT;
    private const int TOP_BAR_HEIGHT = 24;
    private const int EDGE = 4;
    private const int WORLD_HEIGHT = 347;

    public static readonly Rectangle TopBar = new(0, 0, SCREEN_WIDTH, TOP_BAR_HEIGHT);

    /// <summary>The game world: the viewport of the camera. The HUD draws nothing over it but the panels and the chat box.</summary>
    public static readonly Rectangle World = new(EDGE, TOP_BAR_HEIGHT, SCREEN_WIDTH - 2 * EDGE, WORLD_HEIGHT);

    public static readonly Rectangle LeftEdge = new(0, World.Top, EDGE, World.Height);
    public static readonly Rectangle RightEdge = new(World.Right, World.Top, EDGE, World.Height);
    public static readonly Rectangle BottomArea = new(0, World.Bottom, SCREEN_WIDTH, SCREEN_HEIGHT - World.Bottom);

    /// <summary>A large panel: the world area less the inset.</summary>
    public static readonly Rectangle LargePanel = new(
        World.X + PANEL_INSET,
        World.Y + PANEL_INSET,
        World.Width - 2 * PANEL_INSET,
        World.Height - 2 * PANEL_INSET);

    private static readonly int COMPACT_WIDTH = (LargePanel.Width - PANEL_INSET) / 2;

    /// <summary>The compact panel on the left, where a second compact panel opens.</summary>
    public static readonly Rectangle CompactLeft = new(LargePanel.X, LargePanel.Y, COMPACT_WIDTH, LargePanel.Height);

    /// <summary>The compact panel on the right, where one open compact panel is.</summary>
    public static readonly Rectangle CompactRight = new(LargePanel.Right - COMPACT_WIDTH, LargePanel.Y, COMPACT_WIDTH, LargePanel.Height);

    //over the world: the chat box (BRIG-145) at the bottom left, the toast (BRIG-162) at the bottom right
    public static readonly Rectangle ChatBox = new(10, World.Bottom - 124, 215, 118);
    public static readonly Rectangle Toast = new(450, World.Bottom - 28, 186, 28);

    //the bottom area, from the top
    public static readonly Rectangle EffectBar = new(EDGE, BottomArea.Top + 3, World.Width, 20);
    public static readonly Rectangle HealthBar = new(EDGE, BottomArea.Top + 27, 203, 16);
    public static readonly Rectangle ManaBar = new(EDGE, BottomArea.Top + 45, 203, 16);
    public static readonly Rectangle Hotbar = new(210, BottomArea.Top + 26, World.Right - 210, 36);
    public static readonly Rectangle Divider = new(EDGE, BottomArea.Top + 65, World.Width, 1);
    public static readonly Rectangle PlayerPlate = new(EDGE, BottomArea.Top + 70, 125, 24);

    /// <summary>The row of window buttons, right-aligned; the space at its left is for the pet plate (BRIG-4).</summary>
    public static readonly Rectangle WindowButtons = new(260, BottomArea.Top + 70, World.Right - 260, 24);

    public static readonly Rectangle ExperienceBar = new(EDGE, BottomArea.Top + 98, World.Width, SCREEN_HEIGHT - (BottomArea.Top + 98));
}
