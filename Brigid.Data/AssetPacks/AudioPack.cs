#region
using System.IO.Compression;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     Common base for audio <c>.datf</c> packs (music, sound effects). Entries live at the archive root named
///     <c>{prefix}_{id}.{ext}</c>, where <c>{id}</c> is the integer audio id the server references (the value carried
///     in <c>SoundArgs.Sound</c>) and <c>{ext}</c> is any of the pack type's allowed audio extensions. Because the
///     extension varies per entry, the lookup cannot be a direct name probe; at construction the pack scans entry
///     names once and builds an <c>id -&gt; entry-name</c> map (tolerant of zero-padding via <c>int.TryParse</c>).
///     Lookups return the raw, undecoded bytes for the client's audio layer to hand to SDL_mixer — unlike image packs,
///     there is no decode step here.
/// </summary>
public abstract class AudioPack : AssetPack
{
    private readonly Dictionary<int, string> IdToEntry = new();

    private protected AudioPack(
        ZipArchive archive,
        AssetPackManifest manifest,
        string prefix,
        IReadOnlySet<string> allowedExtensions)
        : base(archive, manifest)
    {
        var marker = prefix + "_";

        foreach (var name in EntryNames)
        {
            //entries are flat at the archive root: {prefix}_{id}.{ext}
            if (name.Contains('/'))
                continue;

            if (!name.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
                continue;

            var dot = name.LastIndexOf('.');

            if (dot <= marker.Length)
                continue;

            var ext = name[(dot + 1)..];

            if (!allowedExtensions.Contains(ext))
                continue;

            var idText = name[marker.Length..dot];

            if (!int.TryParse(idText, out var id) || (id <= 0))
                continue;

            //last writer wins on the (pack-author error) case of two extensions for one id
            IdToEntry[id] = name;
        }
    }

    /// <summary>True if this pack carries an entry for <paramref name="id" /> — dictionary lookup only, no read.</summary>
    protected bool HasAudio(int id) => IdToEntry.ContainsKey(id);

    /// <summary>
    ///     Returns the raw bytes of the audio entry for <paramref name="id" />, or false if this pack has no entry for
    ///     it (or the entry failed to read). Caller hands the bytes to SDL_mixer.
    /// </summary>
    protected bool TryGetAudioBytes(int id, out byte[]? bytes)
    {
        bytes = null;

        if (!IdToEntry.TryGetValue(id, out var entryName))
            return false;

        return TryGetEntryBytes(entryName, out bytes);
    }
}
