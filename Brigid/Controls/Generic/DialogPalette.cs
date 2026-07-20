#region
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
    public static readonly Color TabIdleBorder = new(80, 70, 44);
    public static readonly Color TabSelectedBorder = new(210, 176, 96);

    //ability requirements the player does not meet
    public static readonly Color RequirementUnmet = LegendColors.Scarlet;

    //TextButton states
    public static readonly Color ButtonIdleFill = new(30, 32, 42);
    public static readonly Color ButtonHoverFill = new(52, 54, 66);
    public static readonly Color ButtonPressFill = new(74, 58, 22);
    public static readonly Color DisabledText = new(96, 96, 96);
}
