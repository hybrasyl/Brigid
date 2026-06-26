namespace Brigid.Data.Definitions;

public enum VisibleObjectType
{
    Body,
    Face,
    Weapon,
    Armor,
    Armor2,
    Shield,
    Helmet,
    Boots,
    Accessory
}

/// <summary>
///     How an EFA spell effect blends with the scene.
/// </summary>
public enum EffectBlendMode
{
    /// <summary>
    ///     Standard alpha blending (premultiplied).
    /// </summary>
    Normal,

    /// <summary>
    ///     Additive blending: output = src + dst.
    /// </summary>
    Additive,

    /// <summary>
    ///     Screen blending: each color channel is lightened proportionally to the source brightness.
    /// </summary>
    /// <remarks>
    ///     Per-channel formula: output = src + dst * (1 - src). Uses Blend.One + Blend.InverseSourceColor.
    /// </remarks>
    SelfAlpha
}