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
    public const string InputSlot = "input.slot";
    public const string InputSlotFocus = "input.slot.focus";
    public const string CheckBoxNormal = "checkbox.normal";
    public const string CheckBoxHover = "checkbox.hover";
    public const string CheckBoxMark = "checkbox.mark";
    public const string SliderTrack = "slider.track";
    public const string SliderFill = "slider.fill";
    public const string SliderThumb = "slider.thumb";

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
    public const string InputText = "input.text";
    public const string InputFocus = "input.focus";

    //fonts
    public const string TitleFont = "title";
}
