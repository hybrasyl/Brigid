#region
using Brigid.Rendering;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     The one source of truth for how chat text is sized and spaced, shared by the chat display and the chat input so
///     they cannot drift apart — the input sits directly under the display, and a size difference between them reads as
///     a bug even when each is individually reasonable.
///     <para>
///         The size is whatever makes a worst-case line fill the display width at the faces' natural advances. The
///         display owns that width (it comes from game data), so it publishes the result here via
///         <see cref="Recompute" /> and the input follows.
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

    /// <summary>Glyph pixel size chat draws at. Defaults to the UI size until the display first measures itself.</summary>
    public static int Size { get; private set; } = FontEngine.RENDER_SIZE;

    /// <summary>Bumped whenever <see cref="Size" /> changes, so followers can re-apply without polling for equality.</summary>
    public static int Revision { get; private set; }

    /// <summary>
    ///     Recomputes <see cref="Size" /> for the given usable text width. Called by the chat display whenever its width
    ///     or the active face changes.
    /// </summary>
    public static void Recompute(int availableWidth)
    {
        if (availableWidth <= 0)
            return;

        var size = FontEngine.Instance.LargestSizeFitting(BudgetChars, availableWidth, extraSpacing: Spacing);

        if (size == Size)
            return;

        Size = size;
        Revision++;
    }
}
