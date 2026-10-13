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
    public const string MainMenuBackground = "background.mainmenu";
    public const string MainMenuTitle = "title.mainmenu";

    //colours
    public const string Text = "text";
    public const string TextDisabled = "text.disabled";
    public const string TextSelected = "text.selected";
    public const string TextTitle = "text.title";
    public const string Divider = "divider";

    //fonts
    public const string TitleFont = "title";
}
