using System.Runtime.CompilerServices;
using Brigid.Data;
using Brigid.Rendering;
using Brigid.Rendering.Models;
using Microsoft.Xna.Framework.Graphics;
using SkiaSharp;
using Xunit;

namespace Brigid.Tests;

// One unreadable effect frame must not crash effect loading. Its slot stays empty, so the other frames keep their
// indices, center points and timing, and an animation with no frame that renders is rejected. The renderer is injected,
// and textures are identity-only stand-ins, because a real texture needs a graphics device.
[Collection("DataDiagnostics")]
public sealed class EffectFrameSalvageTests
{
    //never drawn or disposed: it only gives a slot a texture identity. Its finalizer is suppressed, because the
    //finalizer of an object that no constructor ran can fault.
    private static Texture2D StandIn()
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        GC.SuppressFinalize(texture);

        return texture;
    }

    private static SpriteFrame Frame(Texture2D? texture, short centerX) => new(texture, centerX, 70, 0, 0);

    private static SKImage OnePixel()
    {
        using var bitmap = new SKBitmap(1, 1);

        return SKImage.FromBitmap(bitmap);
    }

    [Fact]
    public void RenderTexture_ConvertsAFrameThatRenders()
    {
        var texture = StandIn();

        Assert.Same(texture, EffectRenderer.RenderTexture(OnePixel, "frame 0", _ => texture));
    }

    [Fact]
    public void RenderTexture_GivesNoTextureForAFrameWithNoImage()
    {
        var converted = false;

        var texture = EffectRenderer.RenderTexture(
            () => null,
            "frame 1",
            _ =>
            {
                converted = true;

                return StandIn();
            });

        Assert.Null(texture);
        Assert.False(converted);
    }

    [Fact]
    public void RenderTexture_GivesNoTextureAndLogsForAFrameThatThrows()
    {
        var logged = new List<string>();
        var previous = DataDiagnostics.Log;
        DataDiagnostics.Log = logged.Add;

        try
        {
            var texture = EffectRenderer.RenderTexture(() => throw new IndexOutOfRangeException("bad RLE"), "effect 7 frame 2");

            Assert.Null(texture);
            Assert.Contains(logged, line => line.Contains("effect 7 frame 2") && line.Contains("IndexOutOfRangeException"));
        } finally
        {
            DataDiagnostics.Log = previous;
        }
    }

    [Fact]
    public void RenderTexture_LetsATextureUploadFaultThrough()
        => Assert.Throws<InvalidOperationException>(
            () => EffectRenderer.RenderTexture(OnePixel, "frame 3", _ => throw new InvalidOperationException("device lost")));

    [Fact]
    public void HasAnyTexture_RejectsAnAnimationWithNoFrameThatRendered()
        => Assert.False(EffectRenderer.HasAnyTexture([Frame(null, 28), Frame(null, 30)]));

    [Fact]
    public void HasAnyTexture_AcceptsOneBadFrameAmongGoodFrames()
        => Assert.True(EffectRenderer.HasAnyTexture([Frame(StandIn(), 28), Frame(null, 30), Frame(StandIn(), 32)]));

    [Fact]
    public void GetFrame_ABadFrameHoldsThePreviousFrameAndKeepsTheTiming()
    {
        var first = StandIn();
        var third = StandIn();
        var animation = new SpriteAnimation([Frame(first, 28), Frame(null, 30), Frame(third, 32)], 120);

        Assert.Equal(3, animation.FrameCount);
        Assert.Equal(120, animation.FrameIntervalMs);
        Assert.Same(first, animation.GetFrame(1)!.Value.Texture);
        Assert.Equal(28, animation.GetFrame(1)!.Value.CenterX);
        Assert.Same(third, animation.GetFrame(2)!.Value.Texture);
        Assert.Equal(32, animation.GetFrame(2)!.Value.CenterX);
    }

    [Fact]
    public void GetFrame_ALeadingBadFrameDrawsNothing()
    {
        var animation = new SpriteAnimation([Frame(null, 28), Frame(StandIn(), 30)]);

        Assert.Null(animation.GetFrame(0));
        Assert.NotNull(animation.GetFrame(1));
    }
}
