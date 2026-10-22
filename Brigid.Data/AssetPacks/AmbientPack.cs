#region
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     An ambient-sound asset pack backed by a <c>.datf</c> ZIP archive. Filename convention: <c>amb_{id}.{ext}</c> at
///     the archive root, where <c>{id}</c> is the ambient id (1 to 255; 0 means no ambient) and <c>{ext}</c> is one of
///     <c>wav</c>/<c>ogg</c>/<c>mp3</c>/<c>flac</c>. An ambient id is a separate number space from the sound-effect id.
///     There is no legacy fallback. The client decodes these bytes fully, as it does a sound effect, because the bed
///     plays on a mixer channel beside the single music stream. Each bundled SDL2_mixer build decodes all four
///     formats (see <c>SfxPack</c>). An entry that does not decode gives no bed, as there is no fallback.
///     <para>
///     A bed loops unless the manifest says otherwise. <c>covers.ambient_sounds</c> is keyed by ambient id, and only
///     <c>{ "loop": false }</c> changes the default: that bed plays one time.
///     </para>
/// </summary>
public sealed class AmbientPack : AudioPack
{
    private const string CONTENT_TYPE = "ambient_sounds";

    /// <summary>The largest ambient id. The id is a byte, to agree with the planned per-map server field.</summary>
    public const int MAX_AMBIENT_ID = byte.MaxValue;

    private static readonly IReadOnlySet<string> AllowedExtensions =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "wav", "ogg", "mp3", "flac" };

    private readonly HashSet<int> OneShotIds = [];

    internal AmbientPack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest, "amb", AllowedExtensions)
    {
        //a null covers value is legal JSON. treat it as no entries, so that the pack still loads
        if ((manifest.Covers is null)
            || !manifest.Covers.TryGetValue(CONTENT_TYPE, out var coverage)
            || (coverage?.OtherMembers is not { } members))
            return;

        foreach ((var key, var entry) in members)
        {
            if (!int.TryParse(key, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
                continue;

            //only an explicit false makes a one-shot. an absent, true or malformed loop value keeps the default loop
            if ((entry.ValueKind == JsonValueKind.Object)
                && entry.TryGetProperty("loop", out var loop)
                && (loop.ValueKind == JsonValueKind.False))
                OneShotIds.Add(id);
        }
    }

    /// <summary>True when the pack has an entry for <paramref name="ambientId" /> in 1 to <see cref="MAX_AMBIENT_ID" />.</summary>
    public bool Covers(int ambientId) => ambientId is >= 1 and <= MAX_AMBIENT_ID && HasAudio(ambientId);

    /// <summary>
    ///     Attempts to read the ambient-sound bytes for the given <paramref name="ambientId" />. The returned bytes are
    ///     the raw undecoded file; the caller decodes them once via SDL_mixer. An id outside 1 to
    ///     <see cref="MAX_AMBIENT_ID" /> has no bytes, also when the archive has an entry for it.
    /// </summary>
    public bool TryGetAmbientBytes(int ambientId, out byte[]? bytes)
    {
        bytes = null;

        return ambientId is >= 1 and <= MAX_AMBIENT_ID && TryGetAudioBytes(ambientId, out bytes);
    }

    /// <summary>
    ///     Returns false when the manifest marks <paramref name="ambientId" /> as a one-shot with
    ///     <c>{ "loop": false }</c>. Every other id loops, also an id that has no manifest entry.
    /// </summary>
    public bool Loops(int ambientId) => !OneShotIds.Contains(ambientId);
}
