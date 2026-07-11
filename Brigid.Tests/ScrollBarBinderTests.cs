using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Xunit;

namespace Brigid.Tests;

//covers the model->bar mapping the binder owns (thumb geometry + the inverted Value = Max - offset math).
//the bar->model direction goes through ScrollBarControl input handlers and is exercised by the manual UI gate.
public sealed class ScrollBarBinderTests
{
    private static (ScrollModel Model, ScrollBarControl Bar) Bind(bool inverted = false)
    {
        var model = new ScrollModel { Inverted = inverted };
        var bar = new ScrollBarControl();
        _ = new ScrollBarBinder(model, bar); //self-roots via the model/bar event subscriptions

        return (model, bar);
    }

    [Fact]
    public void SetMetrics_PushesExtentViewportMaxOntoBar()
    {
        var (model, bar) = Bind();

        model.SetMetrics(100, 10);

        Assert.Equal(100, bar.TotalItems);
        Assert.Equal(10, bar.VisibleItems);
        Assert.Equal(90, bar.MaxValue);
        Assert.Equal(0, bar.Value);
    }

    [Fact]
    public void OffsetChange_UpdatesBarValue()
    {
        var (model, bar) = Bind();
        model.SetMetrics(100, 10);

        model.ScrollTo(30);

        Assert.Equal(30, bar.Value);
    }

    [Fact]
    public void Inverted_MapsBarValueToMaxMinusOffset_OnMetrics()
    {
        var (model, bar) = Bind(inverted: true);

        model.SetMetrics(100, 10); //Max = 90, offset 0

        //at the start (offset 0) an inverted thumb sits at the far end
        Assert.Equal(90, bar.Value);
    }

    [Fact]
    public void Inverted_MapsBarValueToMaxMinusOffset_OnScroll()
    {
        var (model, bar) = Bind(inverted: true);
        model.SetMetrics(100, 10);

        model.ScrollTo(30);

        Assert.Equal(60, bar.Value); //90 - 30
    }

    [Fact]
    public void MetricsShrink_ReclampsBarValue()
    {
        var (model, bar) = Bind();
        model.SetMetrics(100, 10);
        model.ScrollTo(90);

        model.SetMetrics(20, 10); //Max now 10 → offset reclamps to 10

        Assert.Equal(10, bar.MaxValue);
        Assert.Equal(10, bar.Value);
    }
}
