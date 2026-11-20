#region
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.TestDriver;

/// <summary>
///     Puts input events into SDL with <c>SDL_PushEvent</c>. SDL gives a pushed event to each event watch before the push
///     returns, so <see cref="InputBuffer" /> gets it as it gets a player's event (BRIG-184 phase 0). Each pushed event also
///     stays in SDL's queue for MonoGame, so a key always goes down and then up. The event types, field offsets and
///     modifier bits are the ones that <see cref="Sdl" /> gives <see cref="InputBuffer" />, so the push and the read agree.
///     The pointer does not go through SDL: <see cref="InputBuffer" /> reads the driver's pointer.
/// </summary>
internal static partial class SdlInput
{
    private const string LIBRARY = "SDL2";
    private const int EVENT_SIZE = 56;

    //SDL_CommonEvent: type(4) at 0, timestamp(4), windowID(4) at 8; SDL_KeyboardEvent: state(1) at 12;
    //SDL_MouseButtonEvent: state(1) at 17, clicks(1) at 18
    private const int WINDOW_ID_OFFSET = 8;
    private const int KEYBOARDEVENT_STATE_OFFSET = 12;
    private const int MOUSEBUTTONEVENT_STATE_OFFSET = 17;
    private const int MOUSEBUTTONEVENT_CLICKS_OFFSET = 18;

    //the text field of SDL_TextInputEvent holds 32 bytes, with the terminating zero
    private const int TEXT_BYTES = 31;

    internal const ushort KMOD_LSHIFT = (ushort)Sdl.KMOD_LSHIFT;
    internal const ushort KMOD_LCTRL = (ushort)Sdl.KMOD_LCTRL;
    internal const ushort KMOD_LALT = (ushort)Sdl.KMOD_LALT;
    internal const ushort KMOD_LGUI = (ushort)Sdl.KMOD_LGUI;

    //the lock keys (num, caps, scroll) that a pushed chord keeps as they are
    private const ushort KMOD_LOCKS = 0x1000 | 0x2000 | 0x8000;

    private static readonly Lazy<Dictionary<Keys, int>> Scancodes = new(BuildScancodes);

    /// <summary>The SDL scancode of <paramref name="key" />, from the client's own map in <see cref="InputBuffer" />.</summary>
    internal static bool TryGetScancode(Keys key, out int scancode) => Scancodes.Value.TryGetValue(key, out scancode);

    internal static void PushKey(uint windowId, int scancode, bool down, ushort mods)
    {
        Span<byte> e = stackalloc byte[EVENT_SIZE];
        e.Clear();
        Write(e, 0, down ? Sdl.KEYDOWN : Sdl.KEYUP);
        Write(e, WINDOW_ID_OFFSET, windowId);
        e[KEYBOARDEVENT_STATE_OFFSET] = (byte)(down ? 1 : 0);
        Write(e, Sdl.KEYBOARDEVENT_SCANCODE_OFFSET, scancode);
        BitConverter.TryWriteBytes(e[Sdl.KEYBOARDEVENT_MOD_OFFSET..], mods);
        Push(e);
    }

    internal static void PushText(uint windowId, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        Span<byte> e = stackalloc byte[EVENT_SIZE];

        foreach (var (start, length) in TextChunks(bytes))
        {
            e.Clear();
            Write(e, 0, Sdl.TEXTINPUT);
            Write(e, WINDOW_ID_OFFSET, windowId);
            bytes.AsSpan(start, length).CopyTo(e[Sdl.TEXTINPUTEVENT_TEXT_OFFSET..]);
            Push(e);
        }
    }

    /// <summary>
    ///     Splits UTF-8 text into pieces of at most 31 bytes, the text of one SDL text event, and never inside a
    ///     character.
    /// </summary>
    internal static IEnumerable<(int Start, int Length)> TextChunks(byte[] bytes)
    {
        for (var start = 0; start < bytes.Length;)
        {
            var length = Math.Min(TEXT_BYTES, bytes.Length - start);

            //back up to the first byte of the character that the limit cuts
            while ((length > 0) && (start + length < bytes.Length) && ((bytes[start + length] & 0xC0) == 0x80))
                length--;

            yield return (start, length);
            start += length;
        }
    }

    /// <param name="button"><see cref="Sdl.BUTTON_LEFT" /> or <see cref="Sdl.BUTTON_RIGHT" />.</param>
    internal static void PushButton(uint windowId, byte button, bool down, int windowX, int windowY)
    {
        Span<byte> e = stackalloc byte[EVENT_SIZE];
        e.Clear();
        Write(e, 0, down ? Sdl.MOUSEBUTTONDOWN : Sdl.MOUSEBUTTONUP);
        Write(e, WINDOW_ID_OFFSET, windowId);
        e[Sdl.MOUSEBUTTONEVENT_BUTTON_OFFSET] = button;
        e[MOUSEBUTTONEVENT_STATE_OFFSET] = (byte)(down ? 1 : 0);
        e[MOUSEBUTTONEVENT_CLICKS_OFFSET] = 1;
        Write(e, Sdl.MOUSEBUTTONEVENT_X_OFFSET, windowX);
        Write(e, Sdl.MOUSEBUTTONEVENT_Y_OFFSET, windowY);
        Push(e);
    }

    /// <summary>
    ///     Sets SDL's modifier state to <paramref name="mods" />, with the lock keys as they are, and returns the state
    ///     before. A button event takes its modifiers from this state, and a pushed key does not change it.
    /// </summary>
    internal static ushort SetModifiers(ushort mods)
    {
        var before = (ushort)Sdl.SDL_GetModState();
        SDL_SetModState((ushort)((before & KMOD_LOCKS) | mods));

        return before;
    }

    internal static void RestoreModifiers(ushort state) => SDL_SetModState(state);

    internal static uint WindowId(nint window) => SDL_GetWindowID(window);

    /// <summary>
    ///     Moves the window to a display that is not the primary one (whose bounds do not start at 0, 0), when there is
    ///     one, so that a run does not cover the developer's work. Returns the display index, or -1 when it stays.
    /// </summary>
    internal static int MoveToSecondDisplay(nint window)
    {
        for (var display = 0; display < SDL_GetNumVideoDisplays(); display++)
            if ((Sdl.SDL_GetDisplayBounds(display, out var bounds) == 0) && ((bounds.X != 0) || (bounds.Y != 0)))
            {
                SDL_SetWindowPosition(window, bounds.X + 40, bounds.Y + 40);

                return display;
            }

        return -1;
    }

    private static void Write(Span<byte> e, int offset, uint value) => BitConverter.TryWriteBytes(e[offset..], value);

    private static void Write(Span<byte> e, int offset, int value) => BitConverter.TryWriteBytes(e[offset..], value);

    private static void Push(ReadOnlySpan<byte> e)
    {
        if (SDL_PushEvent(in MemoryMarshal.GetReference(e)) < 0)
            throw new InvalidOperationException("SDL_PushEvent refused the event");
    }

    private static Dictionary<Keys, int> BuildScancodes()
    {
        var map = new Dictionary<Keys, int>();

        //SDL2 scancodes stop below 512
        for (var scancode = 0; scancode < 512; scancode++)
            if (InputBuffer.TranslateScancode(scancode) is var key and not Keys.None)
                map.TryAdd(key, scancode);

        return map;
    }

    //the calls that only the driver makes; the client's own calls are in Sdl
    [LibraryImport(LIBRARY)]
    private static partial int SDL_PushEvent(in byte sdlEvent);

    [LibraryImport(LIBRARY)]
    private static partial void SDL_SetModState(ushort modstate);

    [LibraryImport(LIBRARY)]
    private static partial uint SDL_GetWindowID(nint window);

    [LibraryImport(LIBRARY)]
    private static partial int SDL_GetNumVideoDisplays();

    [LibraryImport(LIBRARY)]
    private static partial void SDL_SetWindowPosition(nint window, int x, int y);
}
