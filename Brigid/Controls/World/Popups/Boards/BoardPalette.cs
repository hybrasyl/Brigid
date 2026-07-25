#region
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups.Boards;

/// <summary>
///     Row colours for the boards/mail panes. The selected row uses light blue here, not the shared gold of
///     <see cref="Generic.DialogPalette" />, so it stays distinct from the yellow "unread/hilight" row that the
///     boards list also shows. Scoped to the boards UI on purpose — the other modernized panels keep the gold.
/// </summary>
internal static class BoardPalette
{
    /// <summary>Selected-row text.</summary>
    public static readonly Color SelectedText = new(120, 196, 255);

    /// <summary>Selected-row background fill (blue analog of <see cref="Generic.DialogPalette.RowSelectedFill" />).</summary>
    public static readonly Color RowSelectedFill = new(22, 40, 74);
}
