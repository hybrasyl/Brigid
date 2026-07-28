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

    private static int Width;
    private static int MeasuredWidth = -1;
    private static int MeasuredGeneration = -1;
    private static int CachedSize = -1;

    /// <summary>
    ///     Records the usable text width chat sizes against. Published by the display, which owns the rect; kept here
    ///     rather than recomputed on demand by the display because the display is a HUD tab and stops updating when
    ///     another tab is shown, while the input bar stays visible and still needs the right size.
    /// </summary>
    public static void SetWidth(int availableWidth)
    {
        if (availableWidth > 0)
            Width = availableWidth;
    }

    /// <summary>
    ///     Glyph pixel size chat draws at, recomputed on demand when the width or the active face has changed. Falls
    ///     back to the UI default until a width has been published.
    /// </summary>
    public static int Size
    {
        get
        {
            if (Width <= 0)
                return CachedSize > 0 ? CachedSize : FontEngine.Instance.UiSize;

            var generation = FontEngine.Instance.Generation;

            if ((Width == MeasuredWidth) && (generation == MeasuredGeneration))
                return CachedSize;

            MeasuredWidth = Width;
            MeasuredGeneration = generation;
            CachedSize = FontEngine.Instance.LargestSizeFitting(BudgetChars, Width, extraSpacing: Spacing);

            return CachedSize;
        }
    }
}
