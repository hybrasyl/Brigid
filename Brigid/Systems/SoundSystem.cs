#region
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Brigid.Data;
using Brigid.Data.AssetPacks;
#endregion

namespace Brigid.Systems;

/// <summary>
///     Manages sound-effect and background-music playback via SDL2_mixer (with minimp3 compiled directly into the
///     mixer DLL). SFX are loaded from legend.dat as MP3 bytes, decoded to PCM once via <c>Mix_LoadWAV_RW</c> and
///     cached as <c>Mix_Chunk</c> pointers; playback uses the mixer's channel pool with per-channel volume. Music
///     is streamed from disk via <c>Mix_LoadMUS</c>, with cross-map transitions driven by SDL_mixer's built-in
///     <c>Mix_FadeOutMusic</c>/<c>Mix_FadeInMusic</c>. When the same sound id is triggered while an earlier
///     instance is still audible, the prior live instances are faded out via <c>Mix_FadeOutChannel</c> (per-id
///     voice-stealing), matching the retail Dark Ages client's behavior of restarting the single cached Miles
///     sample handle on each trigger.
/// </summary>
public sealed class SoundSystem : IDisposable
{
    //the original client opens its Miles driver at 22050 Hz / stereo; we match that rate and let Windows do
    //the single resample to the output device rate rather than stacking our own
    private const int MIX_FREQUENCY = 22050;
    private const int MIX_CHANNELS = 2;
    //sample chunk size fed to the audio callback; ~93ms at 22050Hz, good balance of latency vs callback overhead
    private const int MIX_CHUNK_SIZE = 2048;
    //default Mix_AllocateChannels is 8; we bump to 32 so overlap-heavy situations (AOE effects, crowds of mobs)
    //don't run out of voices and return -1 from Mix_PlayChannel
    private const int CHANNEL_COUNT = 32;
    //fade duration for map-transition music swaps, matched to the feel of the original client's ramp
    private const int MUSIC_FADE_MS = 500;
    private const int MAX_CACHED_SOUNDS = 64;
    private const int VOLUME_STEPS = 10;
    //volume scale multiplier mapping our 0..10 slider to SDL_mixer's 0..128 range; at tick=10 we reach MAX_VOLUME
    private const int VOLUME_SCALE = SdlMixer.MIX_MAX_VOLUME / VOLUME_STEPS;
    //when the same sound id fires while a prior instance is still audible, fade the prior one out over this
    //many milliseconds via Mix_FadeOutChannel. SDL_mixer interpolates the fade sample-accurately inside its mix
    //callback, so there's no step discontinuity in the output waveform
    private const int FADE_OUT_MS = 200;

    //delegate instance kept as a field so the GC doesn't collect the callback SDL holds a native pointer to
    private readonly SdlMixer.ChannelFinishedCallback ChannelFinishedDelegate;
    //populated on SDL's audio thread when a channel naturally finishes; drained in Update() on the game thread
    private readonly ConcurrentQueue<int> FinishedChannels = new();
    //channel → sound id mapping for voice-steal lookup + finish cleanup; only touched on the game thread
    private readonly Dictionary<int, int> ChannelToSoundId = [];
    //inverse lookup: sound id → currently-playing channels, iterated on each PlaySound to fade out prior instances
    private readonly Dictionary<int, List<int>> SoundIdToChannels = [];
    //decoded Mix_Chunk pointers indexed by sound id, with a monotonic timestamp for LRU eviction
    private readonly Dictionary<int, (nint Chunk, long Timestamp)> SoundCache = [];
    //same-frame dedup (e.g. AOE hitting multiple targets in one tick trying to play the same sound N times)
    private readonly HashSet<int> PlayedThisFrame = [];

    private int CurrentMusicId = -1;
    //pins the pack-supplied byte[] backing a streamed Mix_LoadMUS_RW handle; SDL streams from this memory for the
    //life of playback, so it must stay pinned until the matching Mix_FreeMusic. Default (not allocated) when the
    //current track came from a loose file via Mix_LoadMUS. Freed in lockstep with CurrentMusicPtr by FreeCurrentMusic.
    private GCHandle CurrentMusicPin;
    private nint CurrentMusicPtr;
    private bool Initialized;
    private bool IsDisposed;
    private bool MusicFadingOut;
    private int MusicVolumeValue = SdlMixer.MIX_MAX_VOLUME;
    //music id queued to start once the current music's fade-out completes; 0 means "stop music"
    private int PendingMusicId;
    private int SfxVolume = SdlMixer.MIX_MAX_VOLUME;
    private long SoundCacheTimestamp;

    public SoundSystem()
    {
        ChannelFinishedDelegate = OnChannelFinished;
        InitializeMixer();
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (IsDisposed)
            return;

        IsDisposed = true;

        if (!Initialized)
            return;

        //clear the callback BEFORE halting so the audio thread can't fire OnChannelFinished while we tear down
        SdlMixer.Mix_ChannelFinished(nint.Zero);
        SdlMixer.Mix_HaltChannel(SdlMixer.MIX_DEFAULT_CHANNEL);
        SdlMixer.Mix_HaltMusic();

        FreeCurrentMusic();

        foreach (var entry in SoundCache.Values)
            if (entry.Chunk != nint.Zero)
                SdlMixer.Mix_FreeChunk(entry.Chunk);

        SoundCache.Clear();

        SdlMixer.Mix_CloseAudio();
        SdlMixer.Mix_Quit();
        Sdl.SDL_QuitSubSystem(Sdl.SDL_INIT_AUDIO);

        Initialized = false;
    }

    /// <summary>
    ///     Plays background music by id. Triggers a fade-out of the current track (if any) and a fade-in of the new
    ///     track once the fade-out completes. musicId 0 stops playback with a fade-out and leaves no pending track.
    /// </summary>
    public void PlayMusic(int musicId)
    {
        if (IsDisposed || !Initialized)
            return;

        //already mid-fade-out — just update what plays next once the fade completes
        if (MusicFadingOut)
        {
            PendingMusicId = musicId;

            return;
        }

        //already playing the requested track — nothing to do
        if (musicId == CurrentMusicId)
            return;

        //stop request when nothing is currently playing: no fade needed
        if ((musicId == 0) && (CurrentMusicPtr == nint.Zero))
            return;

        //nothing currently playing — skip the fade-out phase and start the new track directly
        if (CurrentMusicPtr == nint.Zero)
        {
            StartMusic(musicId);

            return;
        }

        //kick off the async fade-out; Update() will pick up the completion and start PendingMusicId
        SdlMixer.Mix_FadeOutMusic(MUSIC_FADE_MS);
        MusicFadingOut = true;
        PendingMusicId = musicId;
    }

    /// <summary>
    ///     Plays a sound effect by id. First-time plays decode the MP3 synchronously (minimp3 is fast on short
    ///     files); subsequent plays grab the cached <c>Mix_Chunk</c>. If a previous instance of the same id is
    ///     still playing, it's faded out via <c>Mix_FadeOutChannel</c> so overlaps don't stack loudness.
    /// </summary>
    public void PlaySound(int soundId)
    {
        if (IsDisposed || !Initialized || (SfxVolume <= 0))
            return;

        //collapse same-frame duplicate triggers (e.g. AOE hitting multiple targets in a single tick)
        if (!PlayedThisFrame.Add(soundId))
            return;

        nint chunk;

        if (SoundCache.TryGetValue(soundId, out var cached))
        {
            chunk = cached.Chunk;
            SoundCache[soundId] = (chunk, SoundCacheTimestamp++);
        } else
        {
            chunk = LoadChunk(soundId);

            if (chunk == nint.Zero)
                return;

            SoundCache[soundId] = (chunk, SoundCacheTimestamp++);

            if (SoundCache.Count > MAX_CACHED_SOUNDS)
                EvictOldest();
        }

        //voice-steal any currently-playing instances of the same sound id so overlaps don't stack loudness.
        //Mix_FadeOutChannel does a sample-accurate fade-to-zero-then-halt inside SDL_mixer's mix callback,
        //so the output waveform has no step discontinuity (unlike Mix_Volume, which only takes effect at the
        //next callback boundary and can click when the delta lands on a high-amplitude sample).
        //skip channels the audio thread already finished (not drained yet) — calling Mix_FadeOutChannel on an
        //idle channel would affect whatever play SDL assigns there next.
        //this matches the retail Dark Ages client's per-id voice stealing: it kept exactly one live Miles sample
        //handle per sound id and restarted it on each trigger (AIL_start_sample), so only one instance of any
        //given sound id was audible at a time
        if (SoundIdToChannels.TryGetValue(soundId, out var existing))
            foreach (var ch in existing)
                if (SdlMixer.Mix_Playing(ch) != 0)
                    SdlMixer.Mix_FadeOutChannel(ch, FADE_OUT_MS);

        //manually find a free channel instead of letting Mix_PlayChannel(-1) pick. this lets us set the channel
        //volume BEFORE starting the play, which closes a race where a channel that finished on the audio thread
        //mid-frame (before Update() could reset its volume) would be reassigned here at its old volume; the audio
        //callback could then read the first samples at the wrong level before a post-Mix_PlayChannel reset caught up
        var channel = -1;

        for (var i = 0; i < CHANNEL_COUNT; i++)
            if (SdlMixer.Mix_Playing(i) == 0)
            {
                channel = i;

                break;
            }

        if (channel < 0)
            return;

        //set volume before play begins so the first audio callback after Mix_PlayChannel sees the correct level
        SdlMixer.Mix_Volume(channel, SfxVolume);

        //if the channel we just claimed has stale tracking from its previous play (audio-thread finish was
        //enqueued but not drained yet), scrub it now. the drain will later see ChannelToSoundId[channel] pointing
        //at the NEW sound id (overwritten below) and would otherwise remove the new play's tracking while leaving
        //the previous play's SoundIdToChannels entry permanently stale — stale entries cause spurious voice-steals
        //on unrelated sounds that land on the same channel number later
        if (ChannelToSoundId.Remove(channel, out var prevSoundId))
            if (SoundIdToChannels.TryGetValue(prevSoundId, out var prevList))
            {
                prevList.Remove(channel);

                if (prevList.Count == 0)
                    SoundIdToChannels.Remove(prevSoundId);
            }

        //Mix_PlayChannel with an explicit channel index still stops any sound currently on that channel, but we
        //just verified Mix_Playing(ch) == 0 so the channel is idle
        if (SdlMixer.Mix_PlayChannel(channel, chunk, 0) < 0)
            return;

        ChannelToSoundId[channel] = soundId;

        if (!SoundIdToChannels.TryGetValue(soundId, out var list))
        {
            list = [];
            SoundIdToChannels[soundId] = list;
        }

        if (!list.Contains(channel))
            list.Add(channel);
    }

    /// <summary>
    ///     Sets the music volume. Range: 0 (mute) to 10 (max). Applies immediately to the currently playing track.
    /// </summary>
    public void SetMusicVolume(int volume)
    {
        MusicVolumeValue = Math.Clamp(volume, 0, VOLUME_STEPS) * VOLUME_SCALE;

        if (Initialized)
            SdlMixer.Mix_VolumeMusic(MusicVolumeValue);
    }

    /// <summary>
    ///     Sets the sound effect volume. Range: 0 (mute) to 10 (max). Future plays use the new volume; sounds
    ///     already in flight keep their current channel volume (matching the prior NAudio-based behavior).
    /// </summary>
    public void SetSoundVolume(int volume) => SfxVolume = Math.Clamp(volume, 0, VOLUME_STEPS) * VOLUME_SCALE;

    /// <summary>
    ///     Pumps deferred audio-thread work back into the game state. Call once per frame from the game loop.
    /// </summary>
    public void Update()
    {
        if (IsDisposed || !Initialized)
            return;

        //reset same-frame dedup window; any PlaySound later this frame starts from a clean set
        PlayedThisFrame.Clear();

        //reap channels that finished on the audio thread (either naturally, via Mix_HaltChannel, or at the end
        //of a Mix_FadeOutChannel fade) so their tracking entries don't leak. reset per-channel volume to the
        //current SFX slider here: SDL_mixer preserves channel volume across plays, so leaving a faded-out
        //channel at volume 0 would carry into whatever chunk SDL assigns there next
        while (FinishedChannels.TryDequeue(out var channel))
        {
            //skip stale events for channels that PlaySound already reassigned before this drain ran. the new
            //play already set its own volume and tracking; touching either would corrupt the live sound
            if (SdlMixer.Mix_Playing(channel) != 0)
                continue;

            SdlMixer.Mix_Volume(channel, SfxVolume);

            if (!ChannelToSoundId.Remove(channel, out var soundId))
                continue;

            if (!SoundIdToChannels.TryGetValue(soundId, out var list))
                continue;

            list.Remove(channel);

            if (list.Count == 0)
                SoundIdToChannels.Remove(soundId);
        }

        //detect fade-out completion and start the queued track (if any)
        if (MusicFadingOut && (SdlMixer.Mix_PlayingMusic() == 0))
        {
            MusicFadingOut = false;

            FreeCurrentMusic();

            CurrentMusicId = -1;

            if (PendingMusicId > 0)
                StartMusic(PendingMusicId);

            PendingMusicId = 0;
        }
    }

    private void EvictOldest()
    {
        while (SoundCache.Count > MAX_CACHED_SOUNDS)
        {
            var oldestKey = -1;
            var oldestTime = long.MaxValue;

            foreach ((var key, var entry) in SoundCache)
            {
                //skip anything that's still audibly playing — Mix_FreeChunk on a live chunk corrupts the mixer
                if (SoundIdToChannels.ContainsKey(key))
                    continue;

                if (entry.Timestamp < oldestTime)
                {
                    oldestTime = entry.Timestamp;
                    oldestKey = key;
                }
            }

            //every cached sound is currently playing; defer eviction rather than risk crashing the mixer
            if (oldestKey < 0)
                break;

            var chunk = SoundCache[oldestKey].Chunk;
            SoundCache.Remove(oldestKey);

            if (chunk != nint.Zero)
                SdlMixer.Mix_FreeChunk(chunk);
        }
    }

    private void InitializeMixer()
    {
        if (Sdl.SDL_InitSubSystem(Sdl.SDL_INIT_AUDIO) != 0)
            return;

        //Mix_Init is effectively a no-op for minimp3 (statically linked since SDL_mixer 2.6) but is still part of
        //the official init sequence — safe to call unconditionally
        SdlMixer.Mix_Init(SdlMixer.MIX_INIT_MP3);

        if (SdlMixer.Mix_OpenAudio(MIX_FREQUENCY, SdlMixer.AUDIO_S16LSB, MIX_CHANNELS, MIX_CHUNK_SIZE) != 0)
        {
            SdlMixer.Mix_Quit();
            Sdl.SDL_QuitSubSystem(Sdl.SDL_INIT_AUDIO);

            return;
        }

        SdlMixer.Mix_AllocateChannels(CHANNEL_COUNT);
        SdlMixer.Mix_VolumeMusic(MusicVolumeValue);

        var cb = Marshal.GetFunctionPointerForDelegate(ChannelFinishedDelegate);
        SdlMixer.Mix_ChannelFinished(cb);

        Initialized = true;
    }

    private static nint LoadChunk(int soundId)
    {
        //prefer a .datf sound-effects pack override; a present id replaces the legend.dat sound, a new id adds one
        var pack = AssetPackRegistry.GetSfxPack();

        if ((pack is not null) && pack.TryGetSfxBytes(soundId, out var packBytes) && (packBytes is { Length: > 0 }))
        {
            var packChunk = LoadChunkFromBytes(packBytes);

            if (packChunk != nint.Zero)
                return packChunk;

            //pack entry present but undecodable (corrupt, or a codec the bundled SDL2_mixer wasn't built with —
            //only mp3/wav are guaranteed) — fall through to the legacy sound rather than going silent, matching
            //StartMusic's pack-decode-failure fallback and the image packs' "decode failure = not present" contract
        }

        if (!DatArchives.Legend.TryGetValue($"{soundId}.mp3", out var entry))
            return nint.Zero;

        byte[] bytes;

        try
        {
            using var archiveStream = entry.ToStreamSegment();
            using var ms = new MemoryStream();
            archiveStream.CopyTo(ms);
            bytes = ms.ToArray();
        } catch
        {
            return nint.Zero;
        }

        return LoadChunkFromBytes(bytes);
    }

    private static nint LoadChunkFromBytes(byte[] bytes)
    {
        //pin the managed byte[] so SDL can read it during Mix_LoadWAV_RW; Mix_LoadWAV_RW decodes the whole file
        //to PCM synchronously before returning, after which the buffer can be unpinned and freed
        var handle = GCHandle.Alloc(bytes, GCHandleType.Pinned);

        try
        {
            var rw = Sdl.SDL_RWFromConstMem(handle.AddrOfPinnedObject(), bytes.Length);

            if (rw == nint.Zero)
                return nint.Zero;

            //freesrc=1 asks SDL to close the RWops for us after the load completes
            return SdlMixer.Mix_LoadWAV_RW(rw, 1);
        } finally
        {
            handle.Free();
        }
    }

    private void OnChannelFinished(int channel)
        //called on the SDL audio thread — keep this to a lock-free enqueue so we never touch the tracking dicts
        //from anywhere except the game-loop Update
        => FinishedChannels.Enqueue(channel);

    private void StartMusic(int musicId)
    {
        if (musicId == 0)
            return;

        var handle = nint.Zero;
        var pin = default(GCHandle);

        //prefer a .datf music pack override; a present id replaces the legacy loose .mus, a new id adds a track
        var pack = AssetPackRegistry.GetMusicPack();

        if ((pack is not null) && pack.TryGetMusicBytes(musicId, out var bytes) && (bytes is { Length: > 0 }))
        {
            //SDL streams from this memory for the life of playback, so keep it pinned until Mix_FreeMusic
            pin = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            var rw = Sdl.SDL_RWFromConstMem(pin.AddrOfPinnedObject(), bytes.Length);

            if (rw != nint.Zero)
                //freesrc=1 frees the RWops at Mix_FreeMusic time; the backing byte[] stays pinned via `pin`
                handle = SdlMixer.Mix_LoadMUS_RW(rw, 1);

            if (handle == nint.Zero)
            {
                //pack load failed — release the pin and fall through to the legacy loose-file path
                pin.Free();
                pin = default;
            }
        }

        if (handle == nint.Zero)
        {
            var path = Path.Combine(DataContext.DataPath, "music", $"{musicId}.mus");

            if (File.Exists(path))
                handle = SdlMixer.Mix_LoadMUS(path);
        }

        if (handle == nint.Zero)
        {
            if (pin.IsAllocated)
                pin.Free();

            return;
        }

        CurrentMusicPtr = handle;
        CurrentMusicPin = pin; //default (not allocated) for the loose-file path
        CurrentMusicId = musicId;

        //Mix_FadeInMusic ramps from silence up to the current Mix_VolumeMusic over ms milliseconds
        SdlMixer.Mix_FadeInMusic(handle, -1, MUSIC_FADE_MS);
    }

    //frees the current streaming music handle and, if the track came from a .datf pack, the pinned byte[] backing it.
    //the order matters: Mix_FreeMusic releases SDL's hold on the RWops (and thus the memory) before we unpin
    private void FreeCurrentMusic()
    {
        if (CurrentMusicPtr != nint.Zero)
        {
            SdlMixer.Mix_FreeMusic(CurrentMusicPtr);
            CurrentMusicPtr = nint.Zero;
        }

        if (CurrentMusicPin.IsAllocated)
            CurrentMusicPin.Free();

        CurrentMusicPin = default;
    }
}