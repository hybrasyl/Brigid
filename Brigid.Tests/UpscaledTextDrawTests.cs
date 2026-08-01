#region
using Brigid.Rendering;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     Guards the rule that all text draws in the native pass. Text rasterized into the virtual 640x480 render target
///     is point-upscaled to the backbuffer along with the world, so it renders blurry beside UI text drawn at native
///     size — which is exactly what the debug stats readout did, unnoticed, because blurriness is easy to stop seeing.
///     <para>
///         The draw loop itself needs a graphics device, so what is tested here is the predicate the assertion in
///         <c>DrawLineCore</c> evaluates: layout scale is set every frame from the window ratio, native scale only for
///         the duration of the native pass, so native-1-while-layout-is-not means the draw is in the wrong pass.
///     </para>
/// </summary>
public class UpscaledTextDrawTests
{
    [Theory]
    //non-1:1 window, native scale not applied — text is going into the upscaled target
    [InlineData(1f, 1f, 2f, 2f)]
    [InlineData(1f, 1f, 1.5f, 1.5f)]
    //a single axis is enough: a maximized non-4:3 window stretches unevenly
    [InlineData(1f, 1f, 2f, 1f)]
    [InlineData(1f, 1f, 1f, 2f)]
    public void FlagsTextDrawnOutsideTheNativePass(float nativeX, float nativeY, float layoutX, float layoutY)
        => Assert.True(FontEngine.IsUpscaledTextDraw(nativeX, nativeY, layoutX, layoutY));

    [Theory]
    //inside the native pass: native scale matches the window, so glyphs rasterize at native size
    [InlineData(2f, 2f, 2f, 2f)]
    [InlineData(1.5f, 1.5f, 1.5f, 1.5f)]
    //an exactly 640x480 window: everything is 1:1 and there is no upscale to be blurred by
    [InlineData(1f, 1f, 1f, 1f)]
    public void AcceptsLegitimateDraws(float nativeX, float nativeY, float layoutX, float layoutY)
        => Assert.False(FontEngine.IsUpscaledTextDraw(nativeX, nativeY, layoutX, layoutY));
}
