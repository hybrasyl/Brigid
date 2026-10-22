#region
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
#endregion

namespace Brigid;

/// <summary>
///     P/Invoke declarations for SDL2_mixer 2.8.x. The native binary (SDL2_mixer.dll on Windows) is copied to
///     runtimes/{rid}/native/ via the csproj and resolved by DllResolver. SDL2.dll
///     itself is shipped by MonoGame.Framework.DesktopGL. Since SDL2_mixer 2.6 the MP3 path uses minimp3 compiled
///     directly into SDL2_mixer.dll, so no libmpg123/libogg/libvorbis chain is required on Windows or macOS. The
///     bundled Linux build links the distro's codec libraries (libopusfile, libmpg123, libvorbisfile, libFLAC,
///     libfluidsynth, libmodplug) and fails to load when any is missing.
/// </summary>
internal static partial class SdlMixer
{
    public const string LIBRARY_NAME = "SDL2_mixer";

    //Mix_Init flags (formats that the mixer should initialize support for)
    public const int MIX_INIT_MP3 = 0x00000008;

    //Mix_OpenAudio format constants (little-endian, signed 16-bit PCM — matches most modern output devices)
    public const ushort AUDIO_S16LSB = 0x8010;

    //Mix_PlayChannel / Mix_Volume sentinel values
    public const int MIX_CHANNEL_POST = -2;
    public const int MIX_DEFAULT_CHANNEL = -1;

    //Mix_Volume scale: API takes 0..128 linear; MIX_MAX_VOLUME is defined in SDL_mixer.h as 128
    public const int MIX_MAX_VOLUME = 128;

    //Mix_FadingChannel result (the Mix_Fading enum in SDL_mixer.h)
    public const int MIX_NO_FADING = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void ChannelFinishedCallback(int channel);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void MusicFinishedCallback();

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_Init(int flags);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_Quit();

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_OpenAudio(int frequency, ushort format, int channels, int chunksize);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_CloseAudio();

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_AllocateChannels(int numchans);

    //Mix_LoadWAV_RW despite the name decodes any supported format (WAV/MP3/OGG/FLAC) via the active plugins,
    //dispatched on file magic bytes. freesrc=1 makes SDL close the RWops for us after the load completes.
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint Mix_LoadWAV_RW(nint src, int freesrc);

    //Mix_LoadMUS opens a streaming music handle from a UTF-8 file path. SDL_mixer holds the file open until the
    //music is freed, so we cannot reuse the file before Mix_FreeMusic.
    [LibraryImport(LIBRARY_NAME, StringMarshalling = StringMarshalling.Utf8)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint Mix_LoadMUS(string file);

    //Mix_LoadMUS_RW opens a streaming music handle from an in-memory RWops (used for music supplied by a .datf pack).
    //Unlike Mix_LoadWAV_RW it does NOT fully decode up front — SDL_mixer streams from the RWops's backing memory for
    //the life of playback, so the caller must keep that memory pinned until Mix_FreeMusic. freesrc=1 frees the RWops
    //when the music is freed.
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial nint Mix_LoadMUS_RW(nint src, int freesrc);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_FreeChunk(nint chunk);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_FreeMusic(nint music);

    //Mix_PlayChannel: channel=-1 asks the mixer to pick any available channel; loops=0 plays once
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_PlayChannel(int channel, nint chunk, int loops);

    //Mix_FadeInChannel is a C macro for this call with ticks = -1 (no time limit)
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_FadeInChannelTimed(int channel, nint chunk, int loops, int ms, int ticks);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_HaltChannel(int channel);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_FadeOutChannel(int which, int ms);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_Playing(int channel);

    //MIX_NO_FADING, or 1 (fading out) or 2 (fading in)
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_FadingChannel(int which);

    //Mix_Volume: channel=-1 sets volume for all channels; returns the prior value for the channel
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_Volume(int channel, int volume);

    //Mix_VolumeChunk sets the per-sample default volume (applied each time the chunk plays). We don't use this;
    //we adjust per-channel via Mix_Volume so overlap ducking only affects the live instance.
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_PlayMusic(nint music, int loops);

    //Mix_FadeInMusic starts a music track at volume 0 and ramps to Mix_VolumeMusic over ms milliseconds.
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_FadeInMusic(nint music, int loops, int ms);

    //Mix_FadeOutMusic begins an async fade-out over ms; returns 1 if a fade started, 0 if no music was playing.
    //Music continues until the fade completes, at which point Mix_PlayingMusic returns 0.
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_FadeOutMusic(int ms);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_VolumeMusic(int volume);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_PlayingMusic();

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial int Mix_HaltMusic();

    //Mix_ChannelFinished registers a function-pointer callback that fires on SDL's audio thread each time a
    //channel stops: at the natural end of its chunk, at the end of a fade-out, and at Mix_HaltChannel. Pass 0 to clear.
    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_ChannelFinished(nint cb);

    [LibraryImport(LIBRARY_NAME)]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    public static partial void Mix_HookMusicFinished(nint cb);

}
