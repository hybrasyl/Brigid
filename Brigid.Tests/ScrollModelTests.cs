using Brigid.Controls.Scrolling;
using Xunit;

namespace Brigid.Tests;

public sealed class ScrollModelTests
{
    private static ScrollModel Model(int extent, int viewport)
    {
        var m = new ScrollModel();
        m.SetMetrics(extent, viewport);

        return m;
    }

    [Fact]
    public void New_DefaultsToZeroAndStepOne()
    {
        var m = new ScrollModel();

        Assert.Equal(0, m.Offset);
        Assert.Equal(0, m.Max);
        Assert.Equal(1, m.Step);
        Assert.False(m.CanScroll);
    }

    [Fact]
    public void Max_IsExtentMinusViewport()
    {
        var m = Model(100, 10);

        Assert.Equal(90, m.Max);
        Assert.True(m.CanScroll);
    }

    [Fact]
    public void Max_FloorsAtZeroWhenContentFits()
    {
        var m = Model(8, 10);

        Assert.Equal(0, m.Max);
        Assert.False(m.CanScroll);
    }

    [Fact]
    public void ScrollTo_ClampsAboveMax()
    {
        var m = Model(100, 10);

        Assert.True(m.ScrollTo(1000));
        Assert.Equal(90, m.Offset);
    }

    [Fact]
    public void ScrollTo_ClampsBelowZero()
    {
        var m = Model(100, 10);
        m.ScrollTo(50);

        Assert.True(m.ScrollTo(-5));
        Assert.Equal(0, m.Offset);
    }

    [Fact]
    public void ScrollTo_NoOp_ReturnsFalseAndDoesNotFire()
    {
        var m = Model(100, 10);
        var fires = 0;
        m.Changed += _ => fires++;

        Assert.False(m.ScrollTo(0));
        Assert.Equal(0, fires);
    }

    [Fact]
    public void Changed_FiresWithNewOffset_OnlyOnRealMove()
    {
        var m = Model(100, 10);
        var last = -1;
        var fires = 0;

        m.Changed += o =>
        {
            last = o;
            fires++;
        };

        m.ScrollTo(30);
        m.ScrollTo(30); //no-op

        Assert.Equal(30, last);
        Assert.Equal(1, fires);
    }

    [Fact]
    public void WheelBy_PositiveDelta_ScrollsTowardStart()
    {
        //legacy convention: Value - e.Delta
        var m = Model(100, 10);
        m.ScrollTo(50);

        m.WheelBy(1);

        Assert.Equal(49, m.Offset);
    }

    [Fact]
    public void WheelBy_NegativeDelta_ScrollsTowardEnd()
    {
        var m = Model(100, 10);
        m.ScrollTo(50);

        m.WheelBy(-1);

        Assert.Equal(51, m.Offset);
    }

    [Fact]
    public void WheelBy_MultipliesByStep()
    {
        var m = Model(100, 10);
        m.Step = 5;
        m.ScrollTo(50);

        m.WheelBy(1);

        Assert.Equal(45, m.Offset);
    }

    [Fact]
    public void WheelBy_Inverted_FlipsDirection()
    {
        var m = Model(100, 10);
        m.Inverted = true;
        m.ScrollTo(50);

        m.WheelBy(1); //inverted: positive delta scrolls toward end

        Assert.Equal(51, m.Offset);
    }

    [Fact]
    public void WheelBy_ClampsAtBounds()
    {
        var m = Model(100, 10);
        m.ScrollTo(0);

        Assert.False(m.WheelBy(1)); //already at start
        Assert.Equal(0, m.Offset);
    }

    [Fact]
    public void PageBy_MovesByViewportMinusOne()
    {
        var m = Model(100, 10);
        m.ScrollTo(50);

        m.PageBy(1);

        Assert.Equal(59, m.Offset); //50 + (10 - 1)
    }

    [Fact]
    public void PageBy_Inverted_Flips()
    {
        var m = Model(100, 10);
        m.Inverted = true;
        m.ScrollTo(50);

        m.PageBy(1);

        Assert.Equal(41, m.Offset); //50 - 9
    }

    [Fact]
    public void SetMetrics_ReclampsOffsetIntoNewRange()
    {
        var m = Model(100, 10);
        m.ScrollTo(90);
        var last = -1;
        m.Changed += o => last = o;

        var moved = m.SetMetrics(20, 10); //new Max = 10

        Assert.True(moved);
        Assert.Equal(10, m.Offset);
        Assert.Equal(10, last);
    }

    [Fact]
    public void SetMetrics_NegativeValuesFloorAtZero()
    {
        var m = new ScrollModel();
        m.SetMetrics(-5, -3);

        Assert.Equal(0, m.Max);
        Assert.False(m.CanScroll);
    }

    [Fact]
    public void SetMetrics_RaisesMetricsChanged()
    {
        var m = new ScrollModel();
        var fires = 0;
        m.MetricsChanged += () => fires++;

        m.SetMetrics(100, 10);

        Assert.Equal(1, fires);
    }

    [Fact]
    public void Configure_RaisesMetricsChanged()
    {
        var m = new ScrollModel();
        var fires = 0;
        m.MetricsChanged += () => fires++;

        m.Configure(new FakeSource(extent: 50, viewport: 10, step: 1));

        Assert.Equal(1, fires);
    }

    [Fact]
    public void Configure_CopiesStepAndMetrics()
    {
        var m = Model(100, 10);
        m.ScrollTo(90);

        m.Configure(new FakeSource(extent: 50, viewport: 10, step: 3));

        Assert.Equal(3, m.Step);
        Assert.Equal(40, m.Max);
        Assert.Equal(40, m.Offset); //90 reclamped to new Max
    }

    [Fact]
    public void ScrollToStart_GoesToZero()
    {
        var m = Model(100, 10);
        m.ScrollTo(50);

        Assert.True(m.ScrollToStart());
        Assert.Equal(0, m.Offset);
    }

    [Fact]
    public void ScrollToEnd_GoesToMax()
    {
        var m = Model(100, 10);

        Assert.True(m.ScrollToEnd());
        Assert.Equal(90, m.Offset);
    }

    private sealed class FakeSource(int extent, int viewport, int step) : IScrollSource
    {
        public int ContentExtent => extent;
        public int ViewportExtent => viewport;
        public int Step => step;
        public void ApplyOffset(int offset) { }
    }
}
