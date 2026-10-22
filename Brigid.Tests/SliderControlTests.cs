#region
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-55 primitive slider: a point maps to the nearest step on the track and clamps to the range, a press and a
///     drag change the value and raise the event, <c>SetValue</c> clamps and is silent, and a disabled slider takes no
///     input.
/// </summary>
public class SliderControlTests
{
    //a 146 px slider has a 138 px track, inset half a thumb (4 px) at each end: the legacy pause menu geometry
    private const int WIDTH = 146;
    private const int TRACK = WIDTH - SliderControl.THUMB_W;
    private const int TRACK_START = SliderControl.THUMB_W / 2;

    [Theory]
    [InlineData(TRACK_START, 0)]
    [InlineData(TRACK_START + TRACK, 10)]
    [InlineData(TRACK_START + TRACK / 2, 5)]
    [InlineData(TRACK_START + TRACK * 3 / 10, 3)]
    [InlineData(-50, 0)]
    [InlineData(0, 0)]
    [InlineData(WIDTH, 10)]
    [InlineData(WIDTH + 80, 10)]
    public void APointMapsToTheNearestStepAndClamps(int localX, int expected)
        => Assert.Equal(expected, SliderControl.ValueAt(localX, WIDTH));

    [Fact]
    public void TheThumbIsInsideTheControlAtBothEnds()
    {
        Assert.Equal(0, SliderControl.ThumbX(SliderControl.MIN_VALUE, WIDTH));
        Assert.Equal(WIDTH - SliderControl.THUMB_W, SliderControl.ThumbX(SliderControl.MAX_VALUE, WIDTH));
    }

    [Fact]
    public void SetValueClampsAndRaisesNoEvent()
    {
        var slider = new SliderControl(WIDTH, 14);
        var changes = 0;
        slider.ValueChanged += _ => changes++;

        slider.SetValue(4);
        Assert.Equal(4, slider.Value);

        slider.SetValue(99);
        Assert.Equal(SliderControl.MAX_VALUE, slider.Value);

        slider.SetValue(-3);
        Assert.Equal(SliderControl.MIN_VALUE, slider.Value);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void APressAndADragChangeTheValueAndRaiseTheEvent()
    {
        var slider = new SliderControl(WIDTH, 14);
        var values = new List<int>();
        slider.ValueChanged += values.Add;

        slider.OnMouseDown(new MouseDownEvent { Button = MouseButton.Left, ScreenX = TRACK_START + TRACK / 2 });
        slider.OnMouseMove(new MouseMoveEvent { ScreenX = TRACK_START + TRACK / 5 });
        slider.OnMouseUp(new MouseUpEvent { Button = MouseButton.Left });
        slider.OnMouseMove(new MouseMoveEvent { ScreenX = TRACK_START });

        Assert.Equal([5, 2], values);
        Assert.Equal(2, slider.Value);
    }

    [Fact]
    public void ADisabledSliderTakesNoInput()
    {
        var slider = new SliderControl(WIDTH, 14) { Enabled = false };
        var changes = 0;
        slider.ValueChanged += _ => changes++;

        slider.OnMouseDown(new MouseDownEvent { Button = MouseButton.Left, ScreenX = TRACK_START });
        slider.OnMouseMove(new MouseMoveEvent { ScreenX = TRACK_START + TRACK / 2 });

        Assert.Equal(SliderControl.MAX_VALUE, slider.Value);
        Assert.Equal(0, changes);
    }

    [Fact]
    public void ARightPressDoesNothing()
    {
        var slider = new SliderControl(WIDTH, 14);
        var down = new MouseDownEvent { Button = MouseButton.Right, ScreenX = TRACK_START };

        slider.OnMouseDown(down);

        Assert.False(down.Handled);
        Assert.Equal(SliderControl.MAX_VALUE, slider.Value);
    }
}
