namespace Brigid.Extensions;

public static class ShadowStyleExtensions
{
    extension(ShadowStyle style)
    {
        /// <summary>
        ///     Extra pixels the style's passes occupy beyond the glyph band — X to the right, Y below. Text bounds
        ///     (and any control sized to hold shadowed text) must include this or the outermost pass is clipped off.
        /// </summary>
        public (int X, int Y) ShadowMargin
            => style switch
            {
                ShadowStyle.None                                  => (0, 0),
                ShadowStyle.BottomLeft or ShadowStyle.BottomRight  => (1, 1),
                ShadowStyle.BothSides                             => (2, 1),
                ShadowStyle.Outline                               => (2, 2),
                _                                                 => (0, 0)
            };
    }
}
