#region
using System.IO.Compression;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     A sound-effects asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-id lookup via
///     <see cref="TryGetSfxBytes" />. Filename convention: <c>sfx_{id}.{ext}</c> at the archive root, where
///     <c>{id}</c> is the integer sound id (the value the server sends in <c>SoundArgs.Sound</c> when
///     <c>IsMusic</c> is false; matches the legacy <c>legend.dat</c> <c>{id}.mp3</c> numbering) and <c>{ext}</c> is
///     one of <c>wav</c>/<c>ogg</c>/<c>mp3</c>/<c>flac</c>. A present id replaces the legacy <c>legend.dat</c> sound;
///     a new id is an addition. <c>wav</c> or <c>ogg</c> is recommended for short samples. The client fully decodes
///     these bytes via <c>Mix_LoadWAV_RW</c> and caches the resulting chunk.
/// </summary>
public sealed class SfxPack : AudioPack
{
    private static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wav", "ogg", "mp3", "flac" };

    internal SfxPack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest, "sfx", AllowedExtensions) { }

    /// <summary>
    ///     Attempts to read the sound-effect bytes for the given <paramref name="soundId" />. The returned bytes are
    ///     the raw undecoded file; the caller decodes them once via SDL_mixer and caches the chunk.
    /// </summary>
    public bool TryGetSfxBytes(int soundId, out byte[]? bytes) => TryGetAudioBytes(soundId, out bytes);
}
