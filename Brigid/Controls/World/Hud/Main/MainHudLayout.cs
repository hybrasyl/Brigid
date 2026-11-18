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

    //the boxes of the top bar, from the left (D15, D17, D8)
    private const int BOX_TOP = 4;
    private const int BOX_HEIGHT = 17;
    private const int BOX_PAD = 2;
    private const int BOX_ICON = 13;
    private const int SECURE_ICON = 11;

    private const int BOX_GAP = 3;

    //each box starts after the one before it; the zone box takes the width that the others leave
    public static readonly Rectangle ServerBox = new(EDGE, BOX_TOP, 100, BOX_HEIGHT);
    public static readonly Rectangle GoldBox = After(ServerBox, 62);
    public static readonly Rectangle ClockBox = new(SCREEN_WIDTH - EDGE - 65, BOX_TOP, 65, BOX_HEIGHT);
    public static readonly Rectangle WeightBox = Before(ClockBox, 62);
    public static readonly Rectangle MailBox = Before(WeightBox, 34);
    public static readonly Rectangle ZoneBox = new(GoldBox.Right + BOX_GAP, BOX_TOP, MailBox.Left - BOX_GAP - (GoldBox.Right + BOX_GAP), BOX_HEIGHT);

    /// <summary>The width of a line of a HUD tooltip, and the space between a tooltip and its control.</summary>
    public const int TOOLTIP_TEXT_WIDTH = 220;

    public const int TOOLTIP_GAP = 3;

    private static Rectangle After(Rectangle box, int width) => new(box.Right + BOX_GAP, BOX_TOP, width, BOX_HEIGHT);

    private static Rectangle Before(Rectangle box, int width) => new(box.Left - BOX_GAP - width, BOX_TOP, width, BOX_HEIGHT);

    /// <summary>The place of icon <paramref name="index" /> from the left of a box, in the box.</summary>
    public static Rectangle BoxIconAt(int index) => new(BOX_PAD + index * (BOX_ICON + BOX_PAD), BOX_PAD, BOX_ICON, BOX_ICON);

    //the face centres its full ink (capitals and descenders) in the 12 px text cell. Most top bar text has no descender,
    //so the text area starts 1 px lower, to put the letters at the middle of the box
    private const int BOX_TEXT_DROP = 1;

    /// <summary>The text of a box, in the box: after its icon, or the full box less the padding.</summary>
    public static Rectangle BoxTextIn(Rectangle box, bool withIcon = true)
    {
        var left = withIcon ? 2 * BOX_PAD + BOX_ICON : 2 * BOX_PAD;

        return new Rectangle(left, BOX_TEXT_DROP, box.Width - left - 2 * BOX_PAD, box.Height - BOX_TEXT_DROP);
    }

    /// <summary>The TLS icon at the right of the server box, in the box.</summary>
    public static Rectangle SecureIconIn(Rectangle box)
        => new(box.Width - BOX_PAD - SECURE_ICON, (box.Height - SECURE_ICON) / 2, SECURE_ICON, SECURE_ICON);

    /// <summary>The server name, between the latency icon and the TLS icon, in the box.</summary>
    public static Rectangle ServerNameIn(Rectangle box)
    {
        var text = BoxTextIn(box);

        return text with { Width = text.Width - SECURE_ICON };
    }

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

    private static readonly int CompactWidth = (LargePanel.Width - PANEL_INSET) / 2;

    /// <summary>The compact panel on the left, where a second compact panel opens.</summary>
    public static readonly Rectangle CompactLeft = new(LargePanel.X, LargePanel.Y, CompactWidth, LargePanel.Height);

    /// <summary>The compact panel on the right, where one open compact panel is.</summary>
    public static readonly Rectangle CompactRight = new(LargePanel.Right - CompactWidth, LargePanel.Y, CompactWidth, LargePanel.Height);

    //over the world: the chat box (BRIG-145) at the bottom left, the toast (BRIG-162) at the bottom right
    public static readonly Rectangle ChatBox = new(10, World.Bottom - 124, 248, 118);
    public static readonly Rectangle Toast = new(450, World.Bottom - 28, 186, 28);

    //the parts of the chat box, in the coordinates of the box. While it shows: the tab row at the top, with the hide
    //button at its right, the lines down to the bottom, and the chat input at the bottom while it has the focus. While
    //it is hidden: the tab row at the bottom.
    public const int CHAT_TAB_HEIGHT = 13;
    public const int CHAT_TAB_GAP = 2;
    public const int CHAT_TAB_PAD = 8;
    public const int CHAT_TAB_MIN = 18;
    public const int CHAT_ROW_PAD = 3;
    public const int CHAT_SCROLL_WIDTH = 10;
    public const int CHAT_ROW_HEIGHT = CHAT_TAB_HEIGHT + 2 * CHAT_ROW_PAD;
    public static readonly Rectangle ChatShownRow = new(0, 0, ChatBox.Width, CHAT_ROW_HEIGHT);
    public static readonly Rectangle ChatCollapsedRow = new(0, ChatBox.Height - CHAT_ROW_HEIGHT, ChatBox.Width, CHAT_ROW_HEIGHT);
    public static readonly Rectangle ChatHideButton = new(ChatBox.Width - CHAT_ROW_PAD - 15, CHAT_ROW_PAD, 15, CHAT_TAB_HEIGHT);
    public static readonly Rectangle ChatLog = new(CHAT_ROW_PAD, CHAT_ROW_HEIGHT, ChatBox.Width - 2 * CHAT_ROW_PAD, ChatBox.Height - CHAT_ROW_HEIGHT - CHAT_ROW_PAD);
    public static readonly Rectangle ChatInput = new(CHAT_ROW_PAD, ChatBox.Height - CHAT_ROW_PAD - 15, ChatBox.Width - 2 * CHAT_ROW_PAD, 15);

    /// <summary>The width of the tabs of the chat box: from the left of the row to the hide button.</summary>
    public static int ChatTabRowWidth => ChatHideButton.Left - CHAT_TAB_GAP - CHAT_ROW_PAD;

    /// <summary>
    ///     The widths of a row of tabs: each tab's preferred width while the row fits <paramref name="available" />; else
    ///     the widest tabs shrink, one pixel at a time, down to <see cref="CHAT_TAB_MIN" />. A row that does not fit at the
    ///     minimum keeps the minimum and runs past the end.
    /// </summary>
    public static int[] FitTabs(IReadOnlyList<int> preferred, int available, int gap)
    {
        var widths = preferred.Select(width => Math.Max(width, CHAT_TAB_MIN)).ToArray();
        var budget = available - gap * Math.Max(0, widths.Length - 1);

        while (widths.Sum() > budget)
        {
            var widest = Array.IndexOf(widths, widths.Max());

            if (widths[widest] <= CHAT_TAB_MIN)
                break;

            widths[widest]--;
        }

        return widths;
    }

    //the bottom area, from the top
    public static readonly Rectangle EffectBar = new(EDGE, BottomArea.Top + 3, World.Width, 20);

    /// <summary>The line under the effect bar, in the theme's divider colour (BRIG-147).</summary>
    public static readonly Rectangle EffectDivider = new(EDGE, EffectBar.Bottom + 1, World.Width, 1);
    public static readonly Rectangle HealthBar = new(EDGE, BottomArea.Top + 27, 203, 16);
    public static readonly Rectangle ManaBar = new(EDGE, BottomArea.Top + 45, 203, 16);
    public static readonly Rectangle Hotbar = new(210, BottomArea.Top + 26, World.Right - 210, 36);
    public static readonly Rectangle Divider = new(EDGE, BottomArea.Top + 65, World.Width, 1);
    public static readonly Rectangle PlayerPlate = new(EDGE, BottomArea.Top + 70, 125, 24);

    //the side of the player's icon on the plate: the head of the aisling, at its own pixel size
    private const int PLATE_ICON = 17;

    //the parts of the player plate, in the plate: the icon at the left, then two lines, the name over the level and class
    private const int PLATE_PAD = 3;
    private const int PLATE_TEXT_LEFT = PLATE_PAD + PLATE_ICON + 4;
    public static readonly Rectangle PlateIcon = new(PLATE_PAD, (PlayerPlate.Height - PLATE_ICON) / 2, PLATE_ICON, PLATE_ICON);
    public static readonly Rectangle PlateName = new(PLATE_TEXT_LEFT, 0, PlayerPlate.Width - PLATE_TEXT_LEFT - PLATE_PAD, PlayerPlate.Height / 2);
    public static readonly Rectangle PlateDetail = PlateName with { Y = PlayerPlate.Height / 2 };

    /// <summary>The row of window buttons, right-aligned; the space at its left is for the pet plate (BRIG-4).</summary>
    public static readonly Rectangle WindowButtons = new(260, BottomArea.Top + 70, World.Right - 260, 24);

    //a window button is a small symbol (D29); the symbol size is a placeholder that the layout step can change
    private const int WINDOW_BUTTON = 18;
    private const int WINDOW_BUTTON_PITCH = 30;

    /// <summary>The number of window buttons that the row holds.</summary>
    public static readonly int MaxWindowButtons = WindowButtons.Width / WINDOW_BUTTON_PITCH;

    /// <summary>
    ///     Window button <paramref name="index" /> of <paramref name="count" />, in the row: the row is right-aligned, so it
    ///     lays itself out again when a button is added or removed.
    /// </summary>
    public static Rectangle WindowButtonAt(int index, int count)
        => new(
            WindowButtons.Right - (count - index) * WINDOW_BUTTON_PITCH + (WINDOW_BUTTON_PITCH - WINDOW_BUTTON) / 2,
            WindowButtons.Y + (WindowButtons.Height - WINDOW_BUTTON) / 2,
            WINDOW_BUTTON,
            WINDOW_BUTTON);

    public static readonly Rectangle ExperienceBar = new(EDGE, BottomArea.Top + 98, World.Width, SCREEN_HEIGHT - (BottomArea.Top + 98));

    //the parts of the effect bar (BRIG-147), in the bar: a slot around a half-size spell icon for each effect, from the left
    private const int EFFECT_SLOT = 18;
    private const int EFFECT_PITCH = EFFECT_SLOT + 2;
    private static readonly int EffectPad = (EffectBar.Height - EFFECT_SLOT) / 2;

    /// <summary>The side of the icon of an effect: the half-size spell icon of <see cref="Rendering.UiRenderer" />.</summary>
    public const int EFFECT_ICON = Rendering.UiRenderer.HALF_SPELL_ICON;

    /// <summary>The number of effects that the bar shows; the effects after them wait for a free slot.</summary>
    public static readonly int MaxEffectSlots = (EffectBar.Width - EffectPad) / EFFECT_PITCH;

    /// <summary>The icon of an effect, in its slot.</summary>
    public static readonly Rectangle EffectIconInSlot = new((EFFECT_SLOT - EFFECT_ICON) / 2, (EFFECT_SLOT - EFFECT_ICON) / 2, EFFECT_ICON, EFFECT_ICON);

    /// <summary>Effect slot <paramref name="index" />, in the bar.</summary>
    public static Rectangle EffectSlotAt(int index) => new(EffectPad + index * EFFECT_PITCH, EffectPad, EFFECT_SLOT, EFFECT_SLOT);
}
