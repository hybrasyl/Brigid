namespace Brigid.Rendering;

/// <summary>
///     Typographic style selector for <see cref="FontEngine" /> draw/measure calls. <see cref="Bold" /> and
///     <see cref="Italic" /> are flags that combine and resolve against the target face's style variants, falling back
///     to the regular file when a variant isn't shipped. <see cref="Mono" /> redirects to the dedicated monospace face
///     regardless of the active face (used for code spans), falling back to the active face if no monospace face is
///     present; it composes with the other flags (e.g. bold code), subject to the same variant fallback.
/// </summary>
[Flags]
public enum FontStyle
{
    Regular = 0,
    Bold = 1,
    Italic = 2,
    BoldItalic = Bold | Italic,
    Mono = 4
}
