#region
using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Brigid.Data;
using Brigid.Data.AssetPacks;
using Brigid.Networking;
#endregion

namespace Brigid.Systems;

/// <summary>
///     Manages sound-effect and background-music playback via SDL2_mixer (with minimp3 compiled directly into the
///     mixer DLL). SFX come from the sound-effect pack, else from legend.dat, are decoded to PCM once via
///     <c>Mix_LoadWAV_RW</c> and cached as <c>Mix_Chunk</c> pointers; playback uses the mixer's channel pool with
///     per-channel volume. Music comes from the music pack, else from the loose music files, and streams through
///     <c>Mix_LoadMUS_RW</c> or <c>Mix_LoadMUS</c>, with cross-map transitions driven by SDL_mixer's built-in
///     <c>Mix_FadeOutMusic</c>/<c>Mix_FadeInMusic</c>. When the same sound id is triggered while an earlier
///     instance is still audible, the prior live instances are faded out via <c>Mix_FadeOutChannel</c> (per-id
///     voice-stealing), matching the retail Dark Ages client's behavior of restarting the single cached Miles
///     sample handle on each trigger. An ambient bed (for example rain) comes from the ambient pack, plays on one
///     channel at the ambience volume, and loops unless its pack marks it as a one-shot.
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
    //channel → sound key mapping for voice-steal lookup + finish cleanup; only touched on the game thread
    private readonly Dictionary<int, SoundKey> ChannelToSoundKey = [];
    //inverse lookup: sound key → currently-playing channels, iterated on each PlaySound to fade out prior instances
    private readonly Dictionary<SoundKey, List<int>> SoundKeyToChannels = [];
    //decoded Mix_Chunk pointers indexed by sound key, with a monotonic timestamp for LRU eviction
    private readonly Dictionary<SoundKey, (nint Chunk, long Timestamp)> SoundCache = [];
    //same-frame dedup (e.g. AOE hitting multiple targets in one tick trying to play the same sound N times)
    private readonly HashSet<int> PlayedThisFrame = [];

    //the ambient bed (e.g. rain). while the bed plays, its channel is not in the sound-effect channel tracking
    //above, so per-id voice stealing does not touch it. at a stop, the fading channel goes into that tracking,
    //so the eviction exemption and the finish drain cover the fade. DesiredAmbientId stays after a start that
    //failed because every channel was busy, so Update() can try again. -1 means none. these fields hold
    //ambient ids, not sound-effect ids
    private int AmbientChannel = -1;
    private int PlayingAmbientId = -1;
    private int DesiredAmbientId = -1;
    //a one-shot bed for DesiredAmbientId played to its end. it does not play again until a different id or a stop
    private bool AmbientOneShotDone;
    private int AmbienceVolume = SdlMixer.MIX_MAX_VOLUME;
    //the ambience volume changed while the bed faded in. at the end of a fade-in the mixer sets the volume that it had
    //at the start of the fade, so Update() sets the new volume again when the fade ends
    private bool AmbienceVolumeChangedInFade;

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

    /// <summary>
    ///     True when the SDL2_mixer library, or a codec library it links, could not be loaded, so the client runs
    ///     without sound. Other audio failures (no output device, <c>Mix_OpenAudio</c> failing) leave this false.
    /// </summary>
    public bool MixerLibraryMissing { get; private set; }

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

        AmbientChannel = -1;
        PlayingAmbientId = -1;
        DesiredAmbientId = -1;
        AmbientOneShotDone = false;

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

        var key = SoundKey.Effect(soundId);
        var chunk = GetOrLoadChunk(key);

        if (chunk == nint.Zero)
            return;

        //voice-steal any currently-playing instances of the same sound id so overlaps don't stack loudness.
        //Mix_FadeOutChannel does a sample-accurate fade-to-zero-then-halt inside SDL_mixer's mix callback,
        //so the output waveform has no step discontinuity (unlike Mix_Volume, which only takes effect at the
        //next callback boundary and can click when the delta lands on a high-amplitude sample).
        //skip channels the audio thread already finished (not drained yet) — calling Mix_FadeOutChannel on an
        //idle channel would affect whatever play SDL assigns there next.
        //this matches the retail Dark Ages client's per-id voice stealing: it kept exactly one live Miles sample
        //handle per sound id and restarted it on each trigger (AIL_start_sample), so only one instance of any
        //given sound id was audible at a time
        if (SoundKeyToChannels.TryGetValue(key, out var existing))
            foreach (var ch in existing)
                if (SdlMixer.Mix_Playing(ch) != 0)
                    SdlMixer.Mix_FadeOutChannel(ch, FADE_OUT_MS);

        var channel = ClaimFreeChannel();

        if (channel < 0)
            return;

        //Mix_PlayChannel with an explicit channel index still stops any sound currently on that channel, but
        //ClaimFreeChannel just verified Mix_Playing(ch) == 0 so the channel is idle
        if (SdlMixer.Mix_PlayChannel(channel, chunk, 0) < 0)
            return;

        ChannelToSoundKey[channel] = key;

        if (!SoundKeyToChannels.TryGetValue(key, out var list))
        {
            list = [];
            SoundKeyToChannels[key] = list;
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
    ///     already in flight keep their current channel volume (matching the prior NAudio-based behavior). The
    ///     ambient bed does not use this volume; see <see cref="SetAmbienceVolume" />.
    /// </summary>
    public void SetSoundVolume(int volume) => SfxVolume = Math.Clamp(volume, 0, VOLUME_STEPS) * VOLUME_SCALE;

    /// <summary>
    ///     Sets the ambience volume. Range: 0 (mute) to 10 (max). The ambient bed is continuous, so the new volume
    ///     applies immediately to the bed that plays.
    /// </summary>
    public void SetAmbienceVolume(int volume)
    {
        AmbienceVolume = Math.Clamp(volume, 0, VOLUME_STEPS) * VOLUME_SCALE;

        if (!Initialized || (AmbientChannel < 0))
            return;

        SdlMixer.Mix_Volume(AmbientChannel, AmbienceVolume);

        if (SdlMixer.Mix_FadingChannel(AmbientChannel) != SdlMixer.MIX_NO_FADING)
            AmbienceVolumeChangedInFade = true;
    }

    /// <summary>
    ///     Starts the ambient bed with the given ambient id (e.g. rain), or keeps it running if that id is already
    ///     the bed. A different id replaces the current bed. The audio comes only from the ambient pack: an id with
    ///     no pack entry is a silent no-op. A start that fails only because every mixer channel is busy is retried
    ///     from <see cref="Update" />. Unlike <see cref="PlaySound" />, the bed starts even at ambience volume 0, so
    ///     a later slider-up brings the bed in mid-map. A bed that the pack marks as a one-shot plays one time, and
    ///     does not play again until a different id or a stop. An id of 0 or less stops the bed, because ambient id 0
    ///     means no ambient.
    /// </summary>
    public void StartAmbient(int ambientId)
    {
        if (IsDisposed || !Initialized)
            return;

        if (ambientId <= 0)
        {
            StopAmbient();

            return;
        }

        //this id is already the bed or pending a retry (same-map refresh, rain-to-rain map transition)
        if (ambientId == DesiredAmbientId)
            return;

        StopAmbient();

        DesiredAmbientId = ambientId;
        TryStartAmbient();
    }

    /// <summary>
    ///     Stops the ambient bed, if one plays, with a short fade-out. It is safe to call when no bed plays.
    /// </summary>
    public void StopAmbient()
    {
        DesiredAmbientId = -1;
        AmbientOneShotDone = false;

        if (AmbientChannel < 0)
            return;

        //Dispose resets AmbientChannel before closing the mixer, so reaching here means the mixer is live
        if (SdlMixer.Mix_Playing(AmbientChannel) != 0)
        {
            //the mixer does not fade a channel at volume 0 (Ambience slider at 0), and returns 0. a looping bed then
            //plays on for ever and keeps its channel, so stop it now. the halt is complete under the audio lock, so
            //the chunk is free at once and the channel needs no tracking
            if (SdlMixer.Mix_FadeOutChannel(AmbientChannel, FADE_OUT_MS) == 0)
            {
                SdlMixer.Mix_HaltChannel(AmbientChannel);
                AmbientChannel = -1;
                PlayingAmbientId = -1;

                return;
            }

            //put the fading channel into the sound-effect channel tracking for the fade. EvictOldest then does not
            //free the chunk until the finish drain removes the channel. Mix_FreeChunk on a chunk that the audio
            //thread still mixes corrupts the mixer
            var key = SoundKey.Ambient(PlayingAmbientId);
            ChannelToSoundKey[AmbientChannel] = key;

            if (!SoundKeyToChannels.TryGetValue(key, out var list))
            {
                list = [];
                SoundKeyToChannels[key] = list;
            }

            if (!list.Contains(AmbientChannel))
                list.Add(AmbientChannel);
        }

        AmbientChannel = -1;
        PlayingAmbientId = -1;
    }

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
            ReleaseEndedOneShot(channel);

            if (!ChannelToSoundKey.Remove(channel, out var key))
                continue;

            if (!SoundKeyToChannels.TryGetValue(key, out var list))
                continue;

            list.Remove(channel);

            if (list.Count == 0)
                SoundKeyToChannels.Remove(key);
        }

        //try again to start a bed that did not start because every channel was busy
        if ((DesiredAmbientId >= 0) && (AmbientChannel < 0) && !AmbientOneShotDone)
            TryStartAmbient();

        //the mixer clears the fade and sets the volume of the fade start in one step, so after it the new volume stays
        if (AmbienceVolumeChangedInFade && (AmbientChannel >= 0) && (SdlMixer.Mix_FadingChannel(AmbientChannel) == SdlMixer.MIX_NO_FADING))
        {
            SdlMixer.Mix_Volume(AmbientChannel, AmbienceVolume);
            AmbienceVolumeChangedInFade = false;
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

    //claims an idle channel from the pool, setting its volume before any play begins (a channel that finished
    //on the audio thread mid-frame — before Update() could reset its volume — would otherwise be reassigned at
    //its old level, and the audio callback could read the first samples at the wrong volume) and scrubbing any
    //stale sound-effect tracking left by an undrained finish event (stale entries cause spurious voice-steals on
    //unrelated sounds that land on the same channel number later). returns -1 when every channel is busy
    private int ClaimFreeChannel()
    {
        var channel = -1;

        for (var i = 0; i < CHANNEL_COUNT; i++)
            if (SdlMixer.Mix_Playing(i) == 0)
            {
                channel = i;

                break;
            }

        if (channel < 0)
            return -1;

        //the finish event of a one-shot bed can be still in the queue. release the bed here, or AmbientChannel
        //would point at the sound that takes this channel
        ReleaseEndedOneShot(channel);

        SdlMixer.Mix_Volume(channel, SfxVolume);

        if (ChannelToSoundKey.Remove(channel, out var prevKey))
            if (SoundKeyToChannels.TryGetValue(prevKey, out var prevList))
            {
                prevList.Remove(channel);

                if (prevList.Count == 0)
                    SoundKeyToChannels.Remove(prevKey);
            }

        return channel;
    }

    private void EvictOldest()
    {
        while (SoundCache.Count > MAX_CACHED_SOUNDS)
        {
            SoundKey? oldestKey = null;
            var oldestTime = long.MaxValue;

            foreach ((var key, var entry) in SoundCache)
            {
                //skip anything that's still audibly playing — Mix_FreeChunk on a live chunk corrupts the mixer.
                //the chunk of the playing bed is live but not in SoundKeyToChannels, so it needs its own exemption
                if (SoundKeyToChannels.ContainsKey(key) || (key == SoundKey.Ambient(PlayingAmbientId)))
                    continue;

                if (entry.Timestamp < oldestTime)
                {
                    oldestTime = entry.Timestamp;
                    oldestKey = key;
                }
            }

            //every cached sound is currently playing; defer eviction rather than risk crashing the mixer
            if (oldestKey is not { } evict)
                break;

            var chunk = SoundCache[evict].Chunk;
            SoundCache.Remove(evict);

            if (chunk != nint.Zero)
                SdlMixer.Mix_FreeChunk(chunk);
        }
    }

    //resolves a decoded chunk for the key via the LRU cache, loading and evicting on miss. returns nint.Zero
    //when the key has no audio anywhere
    private nint GetOrLoadChunk(SoundKey key)
    {
        if (SoundCache.TryGetValue(key, out var cached))
        {
            SoundCache[key] = (cached.Chunk, SoundCacheTimestamp++);

            return cached.Chunk;
        }

        var chunk = key.IsAmbient ? LoadAmbientChunk(key.Id) : LoadChunk(key.Id);

        if (chunk == nint.Zero)
            return nint.Zero;

        SoundCache[key] = (chunk, SoundCacheTimestamp++);

        if (SoundCache.Count > MAX_CACHED_SOUNDS)
            EvictOldest();

        return chunk;
    }

    private void InitializeMixer()
    {
        //the bundled Linux mixer links system codec libraries; probe through the p/invokes' resolver so a missing
        //one runs silent instead of throwing DllNotFoundException from the first Mix_* call
        if (!DllResolver.TryResolve(SdlMixer.LIBRARY_NAME))
        {
            MixerLibraryMissing = true;

            return;
        }

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

    //an ambient bed has no legacy fallback: the ambient pack is the only source. there is no fallback to hide a
    //bad entry, so log an entry that is in the pack but does not decode
    private static nint LoadAmbientChunk(int ambientId)
    {
        var pack = AmbientPackFor(ambientId);

        if ((pack is null) || !pack.TryGetAmbientBytes(ambientId, out var bytes) || (bytes is null))
            return nint.Zero;

        if (bytes.Length == 0)
        {
            NoticeDebugLog.Write($"[audio] ambient pack '{pack.Manifest.PackId}' has amb_{ambientId}, but the entry is empty");

            return nint.Zero;
        }

        var chunk = LoadChunkFromBytes(bytes);

        if (chunk == nint.Zero)
            NoticeDebugLog.Write($"[audio] ambient pack '{pack.Manifest.PackId}' has amb_{ambientId}, but it does not decode: {Sdl.GetError()}");

        return chunk;
    }

    //the pack for both the bytes and the loop flag of a bed. use this one seam for both, so that the two cannot
    //come from different packs when the registry holds more than one pack of a content type
    private static AmbientPack? AmbientPackFor(int ambientId) => AssetPackRegistry.GetAmbientPack();

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

    //tries to start the desired bed. a missing asset and a play error do not change, so clear the desire: then the
    //Update() retry does not try each frame. only a full channel pool can change, so then keep the desire, and
    //Update() tries again on the next frame
    private void TryStartAmbient()
    {
        var chunk = GetOrLoadChunk(SoundKey.Ambient(DesiredAmbientId));

        if (chunk == nint.Zero)
        {
            DesiredAmbientId = -1;

            return;
        }

        var channel = ClaimFreeChannel();

        if (channel < 0)
            return;

        //ClaimFreeChannel set the sound-effect volume. set the ambience volume before the play starts, so the
        //first samples do not mix at the wrong level
        SdlMixer.Mix_Volume(channel, AmbienceVolume);

        //loops = -1 repeats until a stop; 0 plays one time (a one-shot bed). the bed is not in the sound-effect
        //channel tracking while it plays, so voice stealing does not touch it. EvictOldest exempts it by
        //PlayingAmbientId
        var loops = AmbientPackFor(DesiredAmbientId)?.Loops(DesiredAmbientId) == false ? 0 : -1;

        //the bed fades in over the same time as it fades out, so that a loud loop does not click at the start
        if (SdlMixer.Mix_FadeInChannelTimed(channel, chunk, loops, FADE_OUT_MS, -1) < 0)
        {
            DesiredAmbientId = -1;

            return;
        }

        AmbientChannel = channel;
        PlayingAmbientId = DesiredAmbientId;
        AmbienceVolumeChangedInFade = false;
    }

    //a looping bed plays until a stop, so an idle AmbientChannel means a one-shot bed came to its end. release the
    //channel, but keep the desire, so that the Update() retry does not play the bed again
    private void ReleaseEndedOneShot(int channel)
    {
        if (channel != AmbientChannel)
            return;

        AmbientChannel = -1;
        PlayingAmbientId = -1;
        AmbientOneShotDone = true;
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
            var path = DataPathResolver.Resolve(DataContext.DataPath, "music", $"{musicId}.mus");

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

    //sound effects and ambient beds have separate id spaces, so the cache and the channel tracking key on both
    private readonly record struct SoundKey(bool IsAmbient, int Id)
    {
        public static SoundKey Effect(int soundId) => new(false, soundId);

        public static SoundKey Ambient(int ambientId) => new(true, ambientId);
    }
}