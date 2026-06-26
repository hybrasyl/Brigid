#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering;

/// <summary>
///     A Texture2D subclass whose Dispose is a no-op. Only the owning TextureCache can release GPU memory via
///     ForceDispose(). This allows cached textures to be freely assigned to UI controls without worrying about
///     double-dispose or premature release.
/// </summary>
public sealed class CachedTexture2D : Texture2D
{
    /// <summary>
    ///     When set, this texture's content lives inside an atlas. AtlasHelper.Draw() uses this to draw the correct sub-region
    ///     from the atlas texture instead of drawing this individual texture.
    /// </summary>
    public AtlasRegion? AtlasRegion { get; internal set; }

    public CachedTexture2D(GraphicsDevice device, int width, int height)
        : base(
            device,
            width,
            height,
            false,
            SurfaceFormat.Color) { }

    protected override void Dispose(bool disposing) { }

    internal void ForceDispose() => base.Dispose(true);
}