#region
using Brigid.Data.Definitions;
#endregion

namespace Brigid.Rendering.Models;

/// <summary>
///     A complete sprite animation (all frames for one animation file). Owns and disposes all contained SpriteFrames.
/// </summary>
public sealed class SpriteAnimation : IDisposable
{
    public EffectBlendMode BlendMode { get; }
    public int FrameIntervalMs { get; }
    public SpriteFrame[] Frames { get; }
    public bool IsEfa { get; }
    public int FrameCount => Frames.Length;

    public SpriteAnimation(
        SpriteFrame[] frames,
        int frameIntervalMs = 100,
        EffectBlendMode blendMode = EffectBlendMode.Normal,
        bool isEfa = false)
    {
        Frames = frames;
        FrameIntervalMs = frameIntervalMs;
        BlendMode = blendMode;
        IsEfa = isEfa;
    }

    public void Dispose()
    {
        foreach (var frame in Frames)
            frame.Dispose();
    }

    public SpriteFrame? GetFrame(int index)
    {
        if ((index < 0) || (index >= Frames.Length))
            return null;

        //if this frame has no texture (skipped during load), use the previous valid frame
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (Frames[index].Texture is not null)
            return Frames[index];

        for (var i = index - 1; i >= 0; i--)

            // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
            if (Frames[i].Texture is not null)
                return Frames[i];

        return null;
    }
}