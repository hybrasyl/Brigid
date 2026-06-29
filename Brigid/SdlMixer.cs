#region
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#endregion

namespace Brigid;

/// <summary>
///     P/Invoke declarations for SDL2_mixer 2.8.x. The native binary (SDL2_mixer.dll on Windows) is copied to
///     runtimes/{rid}/native/ via the csproj and resolved through the standard .NET native library probe. SDL2.dll
///     itself is shipped by MonoGame.Framework.DesktopGL. Since SDL2_mixer 2.6 the MP3 path uses minimp3 compiled
///     directly into SDL2_mixer.dll, so no libmpg123/libogg/libvorbis chain is required.
/// </summary>
internal static partial class SdlMixer
{

    //Mix_Init flags (formats that the mixer should initialize support for)
    public const int MIX_INIT_MP3 = 0x00000008;

    //Mix_OpenAudio format constants (little-endian, signed 16-bit PCM — matches most modern output devices)
    public const ushort AUDIO_S16LSB = 0x8010;

    //Mix_PlayChannel / Mix_Volume sentinel values
    public const int MIX_CHANNEL_POST = -2;
    public const int MIX_DEFAULT_CHANNEL = -1;

    //Mix_Volume scale: API takes 0..128 linear; MIX_MAX_VOLUME is defined in SDL_mixer.h as 128
    public const int MIX_MAX_VOLUME = 128;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ChannelFinishedCallback(int channel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MusicFinishedCallback();

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_Init(int flags);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_Quit();

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_OpenAudio(int frequency, ushort format, int channels, int chunksize);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_CloseAudio();

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_AllocateChannels(int numchans);

    //Mix_LoadWAV_RW despite the name decodes any supported format (WAV/MP3/OGG/FLAC) via the active plugins,
    //dispatched on file magic bytes. freesrc=1 makes SDL close the RWops for us after the load completes.
    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint Mix_LoadWAV_RW(nint src, int freesrc);

    //Mix_LoadMUS opens a streaming music handle from a UTF-8 file path. SDL_mixer holds the file open until the
    //music is freed, so we cannot reuse the file before Mix_FreeMusic.
    [LibraryImport("SDL2_mixer", StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint Mix_LoadMUS(string file);

    //Mix_LoadMUS_RW opens a streaming music handle from an in-memory RWops (used for music supplied by a .datf pack).
    //Unlike Mix_LoadWAV_RW it does NOT fully decode up front — SDL_mixer streams from the RWops's backing memory for
    //the life of playback, so the caller must keep that memory pinned until Mix_FreeMusic. freesrc=1 frees the RWops
    //when the music is freed.
    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint Mix_LoadMUS_RW(nint src, int freesrc);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_FreeChunk(nint chunk);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_FreeMusic(nint music);

    //Mix_PlayChannel: channel=-1 asks the mixer to pick any available channel; loops=0 plays once
    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_PlayChannel(int channel, nint chunk, int loops);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_HaltChannel(int channel);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_FadeOutChannel(int which, int ms);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_Playing(int channel);

    //Mix_Volume: channel=-1 sets volume for all channels; returns the prior value for the channel
    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_Volume(int channel, int volume);

    //Mix_VolumeChunk sets the per-sample default volume (applied each time the chunk plays). We don't use this;
    //we adjust per-channel via Mix_Volume so overlap ducking only affects the live instance.
    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_PlayMusic(nint music, int loops);

    //Mix_FadeInMusic starts a music track at volume 0 and ramps to Mix_VolumeMusic over ms milliseconds.
    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_FadeInMusic(nint music, int loops, int ms);

    //Mix_FadeOutMusic begins an async fade-out over ms; returns 1 if a fade started, 0 if no music was playing.
    //Music continues until the fade completes, at which point Mix_PlayingMusic returns 0.
    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_FadeOutMusic(int ms);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_VolumeMusic(int volume);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_PlayingMusic();

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_HaltMusic();

    //Mix_ChannelFinished registers a function-pointer callback that fires on SDL's audio thread each time a
    //channel's chunk finishes playing naturally (not via Mix_HaltChannel). Pass 0 to clear.
    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_ChannelFinished(nint cb);

    [LibraryImport("SDL2_mixer")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_HookMusicFinished(nint cb);

}
