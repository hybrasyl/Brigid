#region
using System.Runtime.InteropServices;
using Brigid.Networking;
#endregion

namespace Brigid.Utilities;

/// <summary>
///     System clipboard access through SDL2, which the client already loads. SDL serves X11, Wayland, Windows and macOS
///     with no external tool; the TextCopy package it replaced shelled out to <c>xsel</c> on Linux, which Linux Mint
///     does not install, so copy and paste failed there with no sign. Call from the game thread.
/// </summary>
/// <remarks>
///     On X11 the client window owns the selection, so text copied from Brigid stays pasteable only while Brigid runs,
///     unless the desktop runs a clipboard manager.
/// </remarks>
public static class Clipboard
{
    /// <summary>
    ///     Where the text goes. The test assembly swaps in an in-memory backend, because tests run with no SDL video
    ///     subsystem and must not write to the clipboard of the person running them.
    /// </summary>
    internal static IClipboardBackend Backend { get; set; } = new SdlClipboard();

    public static string GetText() => Backend.GetText();

    /// <summary>Puts <paramref name="text" /> on the clipboard. Returns false, and logs why, when it is refused.</summary>
    public static bool SetText(string text) => Backend.SetText(text);

    private sealed class SdlClipboard : IClipboardBackend
    {
        public string GetText()
        {
            //SDL reports a failed read as empty text, as it does an empty clipboard; an error set by this call tells
            //them apart
            Sdl.SDL_ClearError();
            var text = Sdl.SDL_GetClipboardText();

            var result = text == nint.Zero ? string.Empty : (Marshal.PtrToStringUTF8(text) ?? string.Empty);

            if (text != nint.Zero)
                Sdl.SDL_free(text);

            if ((result.Length == 0) && Sdl.GetError() is { Length: > 0 } error)
                NoticeDebugLog.Write($"[clipboard] read failed: {error}");

            return result;
        }

        public bool SetText(string text)
        {
            if (Sdl.SDL_SetClipboardText(text) == 0)
                return true;

            NoticeDebugLog.Write($"[clipboard] write failed: {Sdl.GetError()}");

            return false;
        }
    }
}

internal interface IClipboardBackend
{
    string GetText();

    bool SetText(string text);
}
