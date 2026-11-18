namespace Brigid.Rendering;

/// <summary>
///     The role names that themed primitives look up in a <c>ui_theme</c> pack. A role is a shared look, not one control,
///     so every themed button uses the same <see cref="ButtonNormal" /> art. The names are part of the pack format: a
///     shipped name is published and does not change.
/// </summary>
public static class ThemeRoles
{
    //art parts
    public const string FramePanel = "frame.panel";
    public const string ButtonNormal = "button.normal";
    public const string ButtonHover = "button.hover";
    public const string ButtonPressed = "button.pressed";
    public const string ButtonDisabled = "button.disabled";
    public const string TabNormal = "tab.normal";
    public const string TabSelected = "tab.selected";
    public const string DropDownList = "dropdown.list";
    public const string MainMenuBackground = "background.mainmenu";
    public const string MainMenuTitle = "title.mainmenu";
    public const string StoryBackground = "background.story";
    public const string ScrollTrack = "scrollbar.track";
    public const string ScrollThumb = "scrollbar.thumb";
    public const string ScrollThumbHover = "scrollbar.thumb.hover";
    public const string ScrollThumbPressed = "scrollbar.thumb.pressed";
    public const string ScrollUp = "scrollbar.up";
    public const string ScrollUpPressed = "scrollbar.up.pressed";
    public const string ScrollDown = "scrollbar.down";
    public const string ScrollDownPressed = "scrollbar.down.pressed";
    public const string ScrollHTrack = "scrollbar.htrack";
    public const string ScrollHThumb = "scrollbar.hthumb";
    public const string ScrollHThumbHover = "scrollbar.hthumb.hover";
    public const string ScrollHThumbPressed = "scrollbar.hthumb.pressed";
    public const string ScrollLeft = "scrollbar.left";
    public const string ScrollLeftPressed = "scrollbar.left.pressed";
    public const string ScrollRight = "scrollbar.right";
    public const string ScrollRightPressed = "scrollbar.right.pressed";
    public const string InputSlot = "input.slot";
    public const string InputSlotFocus = "input.slot.focus";
    public const string CheckBoxNormal = "checkbox.normal";
    public const string CheckBoxHover = "checkbox.hover";
    public const string CheckBoxMark = "checkbox.mark";
    public const string SliderTrack = "slider.track";
    public const string SliderFill = "slider.fill";
    public const string SliderThumb = "slider.thumb";
    public const string FrameSign = "frame.sign";
    public const string FramePopup = "frame.popup";

    //the new HUD (BRIG-144): the bar over the world, the area under it, the strips beside it, and a box of the top bar
    public const string HudTopBar = "hud.top_bar";
    public const string HudBottomBar = "hud.bottom_bar";
    public const string HudSideEdge = "hud.side_edge";
    public const string HudBox = "hud.box";
    public const string HudChatBox = "hud.chat_box";
    public const string HudChatScrollTrack = "hud.chat_scroll.track";
    public const string HudChatScrollThumb = "hud.chat_scroll.thumb";

    //the health, mana and experience meters of the new HUD (BRIG-148): a track and a fill for each meter, and the fill
    //of the experience meter at the level cap
    public const string HudHealthTrack = "hud.health.track";
    public const string HudHealthFill = "hud.health.fill";
    public const string HudManaTrack = "hud.mana.track";
    public const string HudManaFill = "hud.mana.fill";
    public const string HudExperienceTrack = "hud.experience.track";
    public const string HudExperienceFill = "hud.experience.fill";
    public const string HudExperienceCapFill = "hud.experience.cap_fill";

    //the effect bar of the new HUD (BRIG-147): the strip, and the slot of each effect
    public const string HudEffectBar = "hud.effect_bar";
    public const string HudEffectSlot = "hud.effect_bar.slot";

    //a slot of the Inventory window of the new HUD (BRIG-150)
    public const string HudInventorySlot = "hud.inventory.slot";

    //content roles: icons, drawn on every host through IconArt, not only on a themed host
    //social status icons, one image for each status
    public const string StatusAwake = "icon.status.awake";
    public const string StatusDoNotDisturb = "icon.status.do_not_disturb";
    public const string StatusDayDreaming = "icon.status.daydreaming";
    public const string StatusNeedGroup = "icon.status.need_group";
    public const string StatusGrouped = "icon.status.grouped";
    public const string StatusLoneHunter = "icon.status.lone_hunter";
    public const string StatusGroupHunting = "icon.status.group_hunting";
    public const string StatusNeedHelp = "icon.status.need_help";

    //the icons of the top bar of the new HUD (BRIG-144)
    public const string IconHudLatencyNone = "icon.hud.latency.none";
    public const string IconHudLatencyGood = "icon.hud.latency.good";
    public const string IconHudLatencyFair = "icon.hud.latency.fair";
    public const string IconHudLatencyPoor = "icon.hud.latency.poor";
    public const string IconHudSecure = "icon.hud.secure";
    public const string IconHudGold = "icon.hud.gold";
    public const string IconHudWeight = "icon.hud.weight";
    public const string IconHudMail = "icon.hud.mail";
    public const string IconHudParcel = "icon.hud.parcel";

    //the buttons of the chat box of the new HUD (BRIG-145)
    public const string IconChatHide = "icon.chat.hide";
    public const string IconChatShow = "icon.chat.show";
    public const string IconChatAdd = "icon.chat.add";

    //the icons of the window buttons of the new HUD (BRIG-144)
    public const string IconWindowAbilities = "icon.window.abilities";
    public const string IconWindowInventory = "icon.window.inventory";
    public const string IconWindowEquipment = "icon.window.equipment";
    public const string IconWindowMastery = "icon.window.mastery";
    public const string IconWindowQuests = "icon.window.quests";
    public const string IconWindowMenu = "icon.window.menu";
    public const string IconWindowMail = "icon.window.mail";
    public const string IconWindowCommunity = "icon.window.community";
    public const string IconWindowMap = "icon.window.map";
    public const string IconWindowGroup = "icon.window.group";
    public const string IconWindowScreenshot = "icon.window.screenshot";

    //the gold bag of the Inventory window of the new HUD (BRIG-150), which a drag drops or gives gold from
    public const string IconInventoryGoldBag = "icon.inventory.gold_bag";

    //colours
    public const string Text = "text";
    public const string TextDisabled = "text.disabled";
    public const string TextSelected = "text.selected";
    public const string TextTitle = "text.title";
    public const string TextError = "text.error";
    public const string Divider = "divider";
    public const string Border = "border";
    public const string RowHover = "row.hover";
    public const string RowSelected = "row.selected";
    public const string RowAlternate = "row.alternate";
    public const string InputText = "input.text";
    public const string InputFocus = "input.focus";

    //fonts
    public const string TitleFont = "title";

    /// <summary>The colour roles, in the order that the primitive gallery shows them.</summary>
    public static readonly string[] Colours =
    [
        Text, TextDisabled, TextSelected, TextTitle, TextError, Divider, Border, RowHover, RowSelected, RowAlternate, InputText, InputFocus
    ];

    /// <summary>
    ///     The surfaces of the new HUD (BRIG-144): its bars, the strips beside the world, the box of the top bar, and the
    ///     chat box with its scroll bar. The HUD shows them, not the primitive gallery, which has no space for the parts of one screen.
    /// </summary>
    public static readonly string[] HudSurfaces =
        [HudTopBar, HudBottomBar, HudSideEdge, HudBox, HudChatBox, HudChatScrollTrack, HudChatScrollThumb];

    /// <summary>The parts of the meters of the new HUD (BRIG-148), which the HUD shows, not the primitive gallery.</summary>
    public static readonly string[] HudMeters =
        [HudHealthTrack, HudHealthFill, HudManaTrack, HudManaFill, HudExperienceTrack, HudExperienceFill, HudExperienceCapFill];

    /// <summary>The parts of the effect bar of the new HUD (BRIG-147), which the HUD shows, not the primitive gallery.</summary>
    public static readonly string[] HudEffects = [HudEffectBar, HudEffectSlot];

    /// <summary>The parts of the Inventory window of the new HUD (BRIG-150), which the HUD shows, not the primitive gallery.</summary>
    public static readonly string[] HudInventory = [HudInventorySlot];

    /// <summary>
    ///     The icons of the window buttons of the new HUD (BRIG-144), in the order of the row. A button is
    ///     <c>icon.window.</c> and the id of its window.
    /// </summary>
    public static readonly string[] WindowIcons =
    [
        IconWindowAbilities, IconWindowInventory, IconWindowEquipment, IconWindowMastery, IconWindowQuests, IconWindowMenu,
        IconWindowMail, IconWindowCommunity, IconWindowMap, IconWindowGroup, IconWindowScreenshot
    ];

    /// <summary>The icons of the top bar of the new HUD, which the HUD shows, not the primitive gallery.</summary>
    public static readonly string[] HudIcons =
    [
        IconHudLatencyNone, IconHudLatencyGood, IconHudLatencyFair, IconHudLatencyPoor, IconHudSecure, IconHudGold, IconHudWeight,
        IconHudMail, IconHudParcel
    ];

    /// <summary>The icons of the chat box of the new HUD, which the HUD shows, not the primitive gallery.</summary>
    public static readonly string[] ChatIcons = [IconChatHide, IconChatShow, IconChatAdd];

    /// <summary>The icons of the Inventory window of the new HUD (BRIG-150), which the HUD shows, not the primitive gallery.</summary>
    public static readonly string[] InventoryIcons = [IconInventoryGoldBag];

    /// <summary>The social status icon parts, indexed by the wire value of the status (awake is 0).</summary>
    public static readonly string[] StatusIcons =
    [
        StatusAwake, StatusDoNotDisturb, StatusDayDreaming, StatusNeedGroup, StatusGrouped, StatusLoneHunter, StatusGroupHunting,
        StatusNeedHelp
    ];
}
