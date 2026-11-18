namespace Brigid.Controls.World.Hud;

/// <summary>
///     The text size and spacing of the chat lines above a chat input, so that the input draws its text as the lines do.
///     The legacy <see cref="Panel.ChatPanel" /> gives its autofit size; the chat box of the new HUD gives the normal UI
///     size and tracking (BRIG-127 D22).
/// </summary>
public interface IChatTextStyle
{
    /// <summary>The glyph size and the extra spacing of the chat lines, with the layout brought up to date first.</summary>
    (int Size, float Spacing) EnsureTextStyle();
}
