#region
using Brigid.Rendering;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     Shared colour palette for the from-scratch (asset-free) modernized dialog panels — the merchant/bank browser
///     (<see cref="World.Popups.Dialog.BankShopPanel" />), the Aisling list
///     (<see cref="World.Popups.WorldList.WorldListControl" />), and the <see cref="TextButton" /> primitive they share.
///     One home so the modernized-dialog look can't drift between panels. Panels keep locally-named constants that alias
///     these values, so a retune happens here once.
/// </summary>
internal static class DialogPalette
{
    //full-window backdrop behind the main menu when no theme supplies a background
    public static readonly Color Backdrop = new(12, 14, 22);

    //window frame + bands
    public static readonly Color FrameFill = new(6, 8, 14, 240);
    public static readonly Color FrameBorder = new(150, 126, 78);
    public static readonly Color Divider = new(64, 56, 36);
    public static readonly Color Title = new(226, 202, 120);

    //list rows + selection
    public static readonly Color RowHoverFill = new(38, 40, 48);
    public static readonly Color RowSelectedFill = new(74, 58, 22);
    public static readonly Color SelectedText = new(255, 208, 96);

    //vertical text tabs
    public static readonly Color TabIdleFill = new(24, 26, 34);
    public static readonly Color TabSelectedFill = new(74, 58, 22);

    //ability requirements the player does not meet
    public static readonly Color RequirementUnmet = LegendColors.Scarlet;

    //the dark box behind a text field or a preview
    public static readonly Color SlotFill = new(0, 0, 0, 205);

    //a form's error line; light enough to read on a dark fill and on theme art
    public static readonly Color ErrorText = new(255, 128, 104);

    //TextButton states
    public static readonly Color ButtonIdleFill = new(30, 32, 42);
    public static readonly Color ButtonHoverFill = new(52, 54, 66);
    public static readonly Color ButtonPressFill = new(74, 58, 22);
    public static readonly Color DisabledText = new(96, 96, 96);

    //the main menu title shadow and status line
    public static readonly Color TitleShadow = new(0, 0, 0, 153);
    public static readonly Color StatusText = new(176, 180, 192);
    public static readonly Color UpdateLink = Color.Red;
    public static readonly Color UpdateLinkHover = Color.OrangeRed;

    //the flat text colour: plain text and the text in a field
    public static readonly Color Text = Color.White;

    //the fill of a focused text field
    public static readonly Color InputFocus = RowHoverFill;

    /// <summary>
    ///     The flat value of a <c>ui_theme</c> colour role: the one colour that every control uses for <paramref name="role" />
    ///     when it is not themed or the theme does not set that colour (BRIG-122 D4).
    /// </summary>
    public static Color ForRole(string role) => role switch
    {
        ThemeRoles.Text         => Text,
        ThemeRoles.TextDisabled => DisabledText,
        ThemeRoles.TextSelected => SelectedText,
        ThemeRoles.TextTitle    => Title,
        ThemeRoles.TextError    => ErrorText,
        ThemeRoles.Divider      => Divider,
        ThemeRoles.Border       => FrameBorder,
        ThemeRoles.RowHover     => RowHoverFill,
        ThemeRoles.RowSelected  => RowSelectedFill,
        ThemeRoles.InputText    => Text,
        ThemeRoles.InputFocus   => InputFocus,
        _                       => throw new ArgumentOutOfRangeException(nameof(role), role, "not a ui_theme colour role")
    };
}
