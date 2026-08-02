#region
using Brigid.Rendering;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     The one source of truth for how chat text is sized and spaced, shared by the chat display and the chat input so
///     they cannot drift apart — the input sits directly under the display, and a size difference between them reads as
///     a bug even when each is individually reasonable.
///     <para>
///         Deliberately stateless. The width chat sizes against belongs to the display, which owns the rect, so the
///         display holds the derived size (<see cref="Panel.ChatPanel.EnsureTextSize" />) and the input asks it. An
///         earlier version cached the width and size in statics here; that made two simultaneously-live HUDs write the
///         same globals with last-writer-wins, correct only because <c>ChattingRect</c>, <c>ChattingRect</c> (large)
///         and <c>ChattingRectExpanded</c> all happen to be 432px wide in <c>setoa.dat</c>.
///     </para>
/// </summary>
internal static class ChatTextStyle
{
    /// <summary>
    ///     Cancels the global negative tracking. None of the shipped faces carry sidebearing slack — glyph ink fills its
    ///     advance cell exactly, and Anonymous Pro's overflows it — so the default -1px pulls each glyph into its
    ///     neighbour rather than taking up slack.
    /// </summary>
    public const float Spacing = -FontEngine.DEFAULT_TRACKING;

    /// <summary>
    ///     Widest line chat is expected to show without wrapping. The server sends chat pre-prefixed as
    ///     "{name}: {message}" (whispers use {name}" / {name}&gt;), names are 4-12 characters by server-side validation
    ///     and the retail input caps the message at 55 — so the budget is 12 + 2 + 55, not 55.
    /// </summary>
    public const int BudgetChars = 12 + 2 + 55;

    /// <summary>
    ///     Glyph pixel size that makes a worst-case chat line fill <paramref name="availableWidth" /> at the faces'
    ///     natural advances. Falls back to the ordinary UI size for a width that is not yet known. Pure — callers cache
    ///     it against their own width and <see cref="FontEngine.Generation" />.
    /// </summary>
    public static int SizeFor(int availableWidth)
        => availableWidth > 0
            ? FontEngine.Instance.LargestSizeFitting(BudgetChars, availableWidth, Spacing)
            : FontEngine.Instance.UiSize;
}
