#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The stepper and colour swatch primitives (BRIG-116): the stepper raises its two events and shows the host's value
///     under literal child names, and a swatch raises a left click only.
/// </summary>
[Collection("FontEngine")]
public class StepperAndSwatchTests
{
    public StepperAndSwatchTests() => FontEngine.Initialize(0);

    [Fact]
    public void TheStepperRaisesPreviousAndNext()
    {
        var stepper = new Stepper(120, 18);
        var steps = new List<string>();
        stepper.Previous += () => steps.Add("previous");
        stepper.Next += () => steps.Add("next");

        stepper.FindChild<TextButton>("previous")!.OnClick(LeftClick());
        stepper.FindChild<TextButton>("next")!.OnClick(LeftClick());

        Assert.Equal(["previous", "next"], steps);
    }

    [Fact]
    public void TheStepperShowsTheValueBetweenItsButtons()
    {
        var stepper = new Stepper(120, 18);

        stepper.SetValue("Style 3/18");

        Assert.Equal("Style 3/18", stepper.ValueText);
        Assert.Equal(["previous", "value", "next"], stepper.Children.Select(child => child.Name));
        Assert.Equal(18, stepper.FindChild<UILabel>("value")!.X);
        Assert.Equal(102, stepper.FindChild<TextButton>("next")!.X);
    }

    [Fact]
    public void ASwatchRaisesOnlyALeftClick()
    {
        var swatch = new ColorSwatch(Color.Teal);
        var clicks = 0;
        swatch.Clicked += () => clicks++;

        swatch.OnClick(new ClickEvent { Button = MouseButton.Right });
        swatch.OnClick(LeftClick());

        Assert.Equal(1, clicks);
    }

    private static ClickEvent LeftClick() => new() { Button = MouseButton.Left };
}
