using System.Runtime.InteropServices;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace Brigid.Tests;

// A slot key uses its slot once a press (BRIG-146): a key down keeps the repeat flag of its SDL event. The other tests
// make the buffered events with the flag set, so they do not read the byte of the event.
[Collection("InputBuffer")]
public class InputBufferKeyRepeatTests
{
    private const int SDL_EVENT_SIZE = 64;
    private const int SCANCODE_A = 4;

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void AKeyDownTakesTheRepeatFlagOfItsSdlEvent(byte repeat, bool expected)
    {
        var sdlEvent = Marshal.AllocHGlobal(SDL_EVENT_SIZE);

        try
        {
            for (var i = 0; i < SDL_EVENT_SIZE; i++)
                Marshal.WriteByte(sdlEvent, i, 0);

            Marshal.WriteInt32(sdlEvent, (int)Sdl.KEYDOWN);
            Marshal.WriteByte(sdlEvent, Sdl.KEYBOARDEVENT_REPEAT_OFFSET, repeat);
            Marshal.WriteInt32(sdlEvent, Sdl.KEYBOARDEVENT_SCANCODE_OFFSET, SCANCODE_A);
            InputBuffer.DiscardPending();

            InputBuffer.HandleKeyEvent(sdlEvent, isDown: true);

            var buffered = Assert.Single(InputBuffer.PendingForTests);
            Assert.Equal(BufferedInputKind.KeyDown, buffered.Kind);
            Assert.Equal(Keys.A, buffered.Key);
            Assert.Equal(expected, buffered.IsRepeat);
        }
        finally
        {
            InputBuffer.HandleKeyEvent(sdlEvent, isDown: false);
            InputBuffer.DiscardPending();
            Marshal.FreeHGlobal(sdlEvent);
        }
    }
}
