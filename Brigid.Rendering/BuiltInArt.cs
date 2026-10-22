#region
using Brigid.Data;
using Brigid.Data.AssetPacks;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     Draws Brigid's own default art (BRIG-123): images that ship in this assembly, keyed by the <c>ui_theme</c> role
///     that a theme part can replace, for example <c>icon.status.awake</c>. A control draws the theme part first, then
///     this art, then its legacy art. The art needs no dat file.
///     <para>
///         An image is the embedded resource <c>Resources/BuiltIn/{role}.png</c>, and its name is a <see cref="ThemeRoles" />
///         role. It is drawn like a theme part with no
///         slice: composited at the native size of its rectangle, contained and centred. A role with no image gives
///         false, and the caller draws its fallback. Call only from the UI thread.
///     </para>
/// </summary>
public static class BuiltInArt
{
    private const string RESOURCE_PREFIX = "Brigid.Rendering.BuiltIn.";
    private const int MAX_CACHED_TEXTURES = 256;

    //the icons are small; the budget only stops a run of window sizes from keeping many large copies
    private const long MAX_CACHED_BYTES = 64L * 1024 * 1024;

    private static readonly UiThemePart Part = new(string.Empty, null, UiThemeFit.Contain, false);

    //the decoded source of each role, or null for a role with no image; the images are small and stay for the run
    private static readonly Dictionary<string, SKImage?> Sources = new(StringComparer.OrdinalIgnoreCase);

    private static readonly ThemeTextureCache<(string Role, int Width, int Height), Texture2D> Textures =
        new(MAX_CACHED_TEXTURES,
            MAX_CACHED_BYTES,
            static texture => (long)texture.Width * texture.Height * 4,
            static () => Environment.TickCount64,
            static texture => texture.Dispose());

    /// <summary>True when Brigid has its own image for <paramref name="role" />.</summary>
    public static bool Has(string role) => GetSource(role) is not null;

    /// <summary>
    ///     Draws the image for <paramref name="role" /> into a logical rectangle, clipped to <paramref name="clip" />. Call
    ///     from the native UI pass. Returns false, and draws nothing, when Brigid has no image for the role.
    /// </summary>
    public static bool TryDraw(SpriteBatch spriteBatch, string role, Rectangle bounds, Rectangle clip, Color? tint = null)
    {
        //most roles have no image, so stop before the cache lookup and its closure
        if (GetSource(role) is null)
            return false;

        var (scaleX, scaleY) = FontEngine.Instance.NativeScale;
        var native = ThemeLayout.ToNative(bounds, scaleX, scaleY);

        if ((native.Width <= 0) || (native.Height <= 0))
            return false;

        var texture = Textures.GetOrCreate(
            (role, native.Width, native.Height),
            () =>
            {
                if (GetSource(role) is not { } source)
                    return null;

                using var composite = ThemeArt.Composite(Part, source, native.Width, native.Height, 1f, 1f);

                return composite is null ? null : TextureConverter.ToTexture2D(composite);
            });

        if (texture is null)
            return false;

        ThemeArt.DrawNative(spriteBatch, texture, native, clip, scaleX, scaleY, tint);

        return true;
    }

    /// <summary>
    ///     Call once at the end of each frame, after the last batch has drawn: textures that the cache evicted in the frame,
    ///     and that are over its byte budget, are disposed at once.
    /// </summary>
    public static void EndFrame() => Textures.EndFrame();

    private static SKImage? GetSource(string role)
    {
        if (Sources.TryGetValue(role, out var source))
            return source;

        using var stream = typeof(BuiltInArt).Assembly.GetManifestResourceStream(RESOURCE_PREFIX + role + ".png");
        source = stream is null ? null : SKImage.FromEncodedData(stream);

        if (stream is not null && source is null)
            DataDiagnostics.Log?.Invoke($"[BuiltInArt] the image for {role} does not decode");

        Sources[role] = source;

        return source;
    }
}
