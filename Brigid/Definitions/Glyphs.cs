namespace Brigid.Definitions;

/// <summary>
///     Single characters used as icons in UI text.
/// </summary>
/// <remarks>
///     Each one is a dependency on a shipped face covering that codepoint — none of the UI faces do, and
///     they resolve through the NotoEmoji fallback added to every <c>FontSystem</c>. A face that lacks
///     the codepoint renders nothing at all rather than failing, so coverage is asserted in
///     <c>ShippedFontsTests</c> rather than left to be noticed on screen.
/// </remarks>
internal static class Glyphs
{
    /// <summary>U+1F512 LOCK. Marks a connection carried over TLS.</summary>
    internal const string PADLOCK = "\U0001F512";
}
