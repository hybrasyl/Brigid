#region
using System.IO.Compression;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     A background-music asset pack backed by a <c>.datf</c> ZIP archive. Exposes per-id lookup via
///     <see cref="TryGetMusicBytes" />. Filename convention: <c>music_{id}.{ext}</c> at the archive root, where
///     <c>{id}</c> is the integer music id (the value the server sends in <c>SoundArgs.Sound</c> when
///     <c>IsMusic</c> is true) and <c>{ext}</c> is one of <c>ogg</c>/<c>mp3</c>/<c>wav</c>/<c>flac</c>/<c>mus</c>.
///     A present id replaces the legacy loose <c>{DataPath}/music/{id}.mus</c>; an id with no legacy counterpart is a
///     new track. <c>ogg</c> is the recommended format. The client streams these bytes via <c>Mix_LoadMUS_RW</c>.
/// </summary>
public sealed class MusicPack : AudioPack
{
    private static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "ogg", "mp3", "wav", "flac", "mus" };

    internal MusicPack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest, "music", AllowedExtensions) { }

    /// <summary>
    ///     Attempts to read the music bytes for the given <paramref name="musicId" />. The returned bytes are the raw
    ///     undecoded file; the caller streams them through SDL_mixer and must keep the buffer pinned for the life of
    ///     playback.
    /// </summary>
    public bool TryGetMusicBytes(int musicId, out byte[]? bytes) => TryGetAudioBytes(musicId, out bytes);
}
