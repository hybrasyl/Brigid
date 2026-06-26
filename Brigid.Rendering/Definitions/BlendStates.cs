#region
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Rendering.Definitions;

/// <summary>
///     Custom blend states used by the rendering pipeline.
/// </summary>
public static class BlendStates
{
    /// <summary>
    ///     Screen blend: output = src + dst * (1 - srcColor) per channel. Each color channel's value acts as its own alpha —
    ///     black pixels are fully transparent, white pixels fully opaque. Matches the original client's blend mode 0x6D. Used
    ///     for foreground tiles with <see cref="DALib.Definitions.TileFlags.Transparent" /> and SelfAlpha EFA effects.
    /// </summary>
    public static readonly BlendState Screen = new()
    {
        ColorSourceBlend = Blend.One,
        ColorDestinationBlend = Blend.InverseSourceColor,
        AlphaSourceBlend = Blend.One,
        AlphaDestinationBlend = Blend.InverseSourceAlpha
    };

    /// <summary>
    ///     Disables all color channel writes. Used for stencil-only passes (e.g. tab map entity masking).
    /// </summary>
    public static readonly BlendState NoColorWrite = new()
    {
        ColorWriteChannels = ColorWriteChannels.None
    };
}