#region
using System.IO.Compression;
using System.Text;
using SkiaSharp;
#endregion

namespace Brigid.Data.AssetPacks;

/// <summary>
///     A UI theme pack backed by a <c>.datf</c> ZIP archive. <c>theme.json</c> at the archive root names each art part,
///     colour and font by role (see <see cref="UiTheme" />); the part images are PNG or WebP entries. Primitive controls
///     that opt in to the theme draw these parts and fall back to their flat look for a role the pack does not supply.
///     A pack with no readable <c>theme.json</c> registers with an empty theme, so every role falls back. Decoded
///     <see cref="SKImage" /> results must be disposed by the caller.
/// </summary>
public sealed class UiThemePack : AssetPack
{
    public const string THEME_ENTRY_NAME = "theme.json";

    public UiTheme Theme { get; }

    internal UiThemePack(ZipArchive archive, AssetPackManifest manifest)
        : base(archive, manifest)
    {
        void Warn(string message) => DataDiagnostics.Log?.Invoke($"[asset-pack] ui_theme '{manifest.PackId}': {message}");

        UiTheme? theme = null;

        if (TryGetEntryBytes(THEME_ENTRY_NAME, out var bytes) && bytes is not null)
            theme = UiTheme.Parse(Encoding.UTF8.GetString(bytes), Warn);
        else
            Warn($"no {THEME_ENTRY_NAME}; every role uses the flat look");

        Theme = theme ?? UiTheme.Empty;
    }

    /// <summary>
    ///     Gets the part for <paramref name="role" /> and decodes its image. Returns false when the theme has no such
    ///     role, or its entry is absent or does not decode — the caller then draws its flat look.
    /// </summary>
    public bool TryGetPartImage(string role, out UiThemePart? part, out SKImage? image)
    {
        image = null;

        if (!Theme.Parts.TryGetValue(role, out part))
            return false;

        if (TryGetImage(part.File, out image))
            return true;

        DataDiagnostics.Log?.Invoke($"[asset-pack] ui_theme '{Manifest.PackId}': part '{role}' entry '{part.File}' is absent or does not decode");
        part = null;

        return false;
    }

    /// <summary>Gets the raw bytes of the font for <paramref name="role" />, for example <c>title</c>.</summary>
    public bool TryGetFontBytes(string role, out byte[]? bytes)
    {
        bytes = null;

        return Theme.Fonts.TryGetValue(role, out var file) && TryGetEntryBytes(file, out bytes);
    }
}
