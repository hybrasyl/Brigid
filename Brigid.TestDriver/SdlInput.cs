#region
using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.TestDriver;

/// <summary>
///     Puts input events into SDL with <c>SDL_PushEvent</c>. SDL gives a pushed event to each event watch before the push
///     returns, so <see cref="InputBuffer" /> gets it as it gets a player's event (BRIG-184 phase 0). Each pushed event also
///     stays in SDL's queue for MonoGame, so a key always goes down and then up.
/// </summary>
internal static partial class SdlInput
{
    private const string LIBRARY = "SDL2";
    private const int EVENT_SIZE = 56;

    private const uint KEYDOWN = 0x300;
    private const uint KEYUP = 0x301;
    private const uint TEXTINPUT = 0x303;
    private const uint MOUSEMOTION = 0x400;
    private const uint MOUSEBUTTONDOWN = 0x401;
    private const uint MOUSEBUTTONUP = 0x402;
    private const uint MOUSEWHEEL = 0x403;

    //the text field of SDL_TextInputEvent holds 32 bytes, with the terminating zero
    private const int TEXT_BYTES = 31;

    internal const ushort KMOD_LSHIFT = 0x0001;
    internal const ushort KMOD_LCTRL = 0x0040;
    internal const ushort KMOD_LALT = 0x0100;
    internal const ushort KMOD_LGUI = 0x0400;

    //the lock keys (num, caps, scroll) that a pushed chord keeps as they are
    private const ushort KMOD_LOCKS = 0x1000 | 0x2000 | 0x8000;

    private static readonly Lazy<Dictionary<Keys, int>> Scancodes = new(BuildScancodes);

    /// <summary>The SDL scancode of <paramref name="key" />, from the client's own map in <see cref="InputBuffer" />.</summary>
    internal static bool TryGetScancode(Keys key, out int scancode) => Scancodes.Value.TryGetValue(key, out scancode);

    internal static void PushKey(uint windowId, int scancode, bool down, ushort mods)
    {
        Span<byte> e = stackalloc byte[EVENT_SIZE];
        e.Clear();
        Write(e, 0, down ? KEYDOWN : KEYUP);
        Write(e, 8, windowId);
        e[12] = (byte)(down ? 1 : 0);
        Write(e, 16, scancode);
        BitConverter.TryWriteBytes(e[24..], mods);
        Push(e);
    }

    internal static void PushText(uint windowId, string text)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        Span<byte> e = stackalloc byte[EVENT_SIZE];

        foreach (var (start, length) in TextChunks(bytes))
        {
            e.Clear();
            Write(e, 0, TEXTINPUT);
            Write(e, 8, windowId);
            bytes.AsSpan(start, length).CopyTo(e[12..]);
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

    /// <summary>A motion event. SDL's own cursor does not move; <see cref="InputBuffer" /> reads the driver's pointer.</summary>
    internal static void PushMotion(uint windowId, int windowX, int windowY)
    {
        Span<byte> e = stackalloc byte[EVENT_SIZE];
        e.Clear();
        Write(e, 0, MOUSEMOTION);
        Write(e, 8, windowId);
        Write(e, 20, windowX);
        Write(e, 24, windowY);
        Push(e);
    }

    /// <param name="button">1 for the left button, 3 for the right button.</param>
    internal static void PushButton(uint windowId, byte button, bool down, int windowX, int windowY)
    {
        Span<byte> e = stackalloc byte[EVENT_SIZE];
        e.Clear();
        Write(e, 0, down ? MOUSEBUTTONDOWN : MOUSEBUTTONUP);
        Write(e, 8, windowId);
        e[16] = button;
        e[17] = (byte)(down ? 1 : 0);
        e[18] = 1;
        Write(e, 20, windowX);
        Write(e, 24, windowY);
        Push(e);
    }

    internal static void PushWheel(uint windowId, int notches)
    {
        Span<byte> e = stackalloc byte[EVENT_SIZE];
        e.Clear();
        Write(e, 0, MOUSEWHEEL);
        Write(e, 8, windowId);
        Write(e, 20, notches);
        Push(e);
    }

    /// <summary>
    ///     Sets SDL's modifier state to <paramref name="mods" />, with the lock keys as they are, and returns the state
    ///     before. A button event takes its modifiers from this state, and a pushed key does not change it.
    /// </summary>
    internal static ushort SetModifiers(ushort mods)
    {
        var before = SDL_GetModState();
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
            if ((SDL_GetDisplayBounds(display, out var bounds) == 0) && ((bounds.X != 0) || (bounds.Y != 0)))
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

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int X;
        public int Y;
        public int W;
        public int H;
    }

    [LibraryImport(LIBRARY)]
    private static partial int SDL_PushEvent(in byte sdlEvent);

    [LibraryImport(LIBRARY)]
    private static partial ushort SDL_GetModState();

    [LibraryImport(LIBRARY)]
    private static partial void SDL_SetModState(ushort modstate);

    [LibraryImport(LIBRARY)]
    private static partial uint SDL_GetWindowID(nint window);

    [LibraryImport(LIBRARY)]
    private static partial int SDL_GetNumVideoDisplays();

    [LibraryImport(LIBRARY)]
    private static partial int SDL_GetDisplayBounds(int displayIndex, out Rect rect);

    [LibraryImport(LIBRARY)]
    private static partial void SDL_SetWindowPosition(nint window, int x, int y);
}
