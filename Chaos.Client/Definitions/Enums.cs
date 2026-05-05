namespace Chaos.Client.Definitions;

public enum HorizontalAlignment
{
    Left,
    Center,
    Right
}

public enum VerticalAlignment
{
    Center,
    Top,
    Bottom
}

public enum ShadowStyle
{
    None,
    BottomLeft,
    BottomRight,
    BothSides
}

public enum PopupStyle
{
    Scroll,
    NonScroll,
    Wooden
}

/// <summary>
///     Identifies which panel is active in the shared "center bottom" tab area of the HUD. Only one tab panel is visible
///     at a time.
/// </summary>
public enum HudTab
{
    Inventory,
    Skills,
    Spells,
    SkillsAlt,
    SpellsAlt,
    Chat,
    Stats,
    ExtendedStats,
    Tools,
    MessageHistory
}

/// <summary>
///     Selects which page of a knowledge book a SkillBookPanel/SpellBookPanel views. The enum value is the
///     slot offset into the book. Each page contains 35 usable slots inside a 36-cell address space — wire
///     slots 36 and 72 are intentionally skipped page-divider markers, which is why <see cref="Page2" /> = 36
///     and <see cref="Page3" /> = 72 rather than 35 and 70. SkillBook is 89 slots, SpellBook is 90.
/// </summary>
public enum SkillBookPage : byte
{
    /// <summary>Wire slots 1–35.</summary>
    Page1 = 0,

    /// <summary>Wire slots 37–71 (Shift+S / Shift+D alt panels).</summary>
    Page2 = 36,

    /// <summary>Wire slots 73–88 (skills) / 73–89 (spells). World abilities shown on the H tab.</summary>
    Page3 = 72
}

/// <summary>
///     Cooldown rendering style for panel slots.
/// </summary>
public enum CooldownStyle
{
    /// <summary>
    ///     No cooldown rendering.
    /// </summary>
    None,

    /// <summary>
    ///     Spell-style: a blue-tinted copy of the icon replaces the normal icon for the full duration. Matches retail
    ///     <c>SpellInvItemPane::Render</c> which applies a 50/50 blend with <c>legend.pal[0x58]</c> over the entire icon.
    /// </summary>
    Swap,

    /// <summary>
    ///     Skill-style: a grey-tinted base (50/50 blend with <c>legend.pal[0x18]</c>) overlaid by a blue-tinted copy
    ///     (50/50 blend with <c>legend.pal[0x58]</c>) that is progressively revealed top-to-bottom as the cooldown
    ///     elapses. Matches retail <c>SkillInvItemPane::Render</c>.
    /// </summary>
    Progressive
}

/// <summary>
///     The current animation state of a world entity.
/// </summary>
public enum EntityAnimState : byte
{
    Idle,
    Walking,
    BodyAnim
}

/// <summary>
///     The available tabs in the status book.
/// </summary>
public enum StatusBookTab
{
    Equipment,
    Legend,
    Skills,
    Events,
    Album,
    Family
}

/// <summary>
///     Determines which icon variant to display for an ability in the metadata tab.
/// </summary>
public enum AbilityIconState
{
    /// <summary>
    ///     Player already knows this ability — use the standard icon (001 variant).
    /// </summary>
    Known,

    /// <summary>
    ///     Player meets all requirements to learn this ability — use the learnable icon (002 variant).
    /// </summary>
    Learnable,

    /// <summary>
    ///     Player does not meet the requirements — use the locked icon (003 variant).
    /// </summary>
    Locked
}

/// <summary>
///     Determines the display state of an event entry in the events metadata tab.
/// </summary>
public enum EventState
{
    /// <summary>
    ///     Player does not meet circle, class, or prerequisite requirements — gray text.
    /// </summary>
    Unavailable,

    /// <summary>
    ///     Player meets all requirements and can complete this event — blue text.
    /// </summary>
    Available,

    /// <summary>
    ///     Player has a legend mark matching this event's ID — green text.
    /// </summary>
    Completed
}

/// <summary>
///     Scrollbar orientation.
/// </summary>
public enum ScrollOrientation
{
    Vertical,
    Horizontal
}