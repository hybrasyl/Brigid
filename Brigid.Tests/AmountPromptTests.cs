#region
using Brigid.Controls.Components;
using Brigid.Controls.World.Popups;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Xunit;
#endregion

namespace Brigid.Tests;

/// <summary>
///     The BRIG-122 amount prompts: every child has a lowercase name, the field takes digits only, OK is disabled while
///     the field is empty, Enter confirms an amount more than 0, and each close raises <c>OnClose</c> once.
/// </summary>
[Collection("FontEngine")]
public class AmountPromptTests
{
    public AmountPromptTests() => FontEngine.Initialize(0);

    private static void Type(UITextBox box, string text)
    {
        foreach (var c in text)
            box.OnTextInput(new TextInputEvent { Character = c });
    }

    [Fact]
    public void EveryChildHasAUniqueName()
    {
        UiAssert.NamedTree(new ItemAmountControl());
        UiAssert.NamedTree(new GoldAmountControl());
    }

    [Fact]
    public void TheFieldTakesDigitsOnly()
    {
        var prompt = new GoldAmountControl();
        prompt.Show();

        Type(prompt.AmountTextBox, "1a2-3 4");

        Assert.Equal("1234", prompt.AmountTextBox.Text);
    }

    [Fact]
    public void TheFieldKeepsItsLimit()
    {
        var prompt = new ItemAmountControl();
        prompt.ShowForSlot(5);

        Type(prompt.AmountTextBox, "12345");

        Assert.Equal("123", prompt.AmountTextBox.Text);
        Assert.Equal(5, prompt.ItemSlot);
    }

    [Fact]
    public void OkIsDisabledWhileTheFieldIsEmpty()
    {
        var prompt = new ItemAmountControl();
        prompt.ShowForSlot(1);
        prompt.Update(new GameTime());

        Assert.False(prompt.OkButton.Enabled);

        Type(prompt.AmountTextBox, "7");
        prompt.Update(new GameTime());

        Assert.True(prompt.OkButton.Enabled);
    }

    [Theory]
    [InlineData("250", 250u)]
    [InlineData("0", null)]
    [InlineData("9999999999", uint.MaxValue)]
    public void EnterConfirmsAnAmountMoreThanZero(string typed, uint? expected)
    {
        var prompt = new GoldAmountControl();
        uint? confirmed = null;
        var closes = 0;
        prompt.OnConfirm += amount => confirmed = amount;
        prompt.OnClose += () => closes++;
        prompt.ShowForTarget(42, 3, 4);
        Type(prompt.AmountTextBox, typed);

        var e = new KeyDownEvent { Key = Keys.Enter };
        prompt.OnKeyDown(e);

        Assert.True(e.Handled);
        Assert.False(prompt.Visible);
        Assert.Equal(expected, confirmed);
        Assert.Equal(1, closes);
        Assert.Equal(42u, prompt.TargetEntityId);
        Assert.Equal(3, prompt.TargetTileX);
        Assert.Equal(4, prompt.TargetTileY);
    }

    [Fact]
    public void TheExchangeTargetReplacesADropTargetAndBack()
    {
        var prompt = new GoldAmountControl();

        prompt.ShowForTarget(42, 3, 4);
        Assert.False(prompt.ForExchange);

        prompt.Hide();
        prompt.ShowForExchange();

        Assert.True(prompt.ForExchange);
        Assert.Null(prompt.TargetEntityId);

        prompt.Hide();
        prompt.ShowForTarget(null, 5, 6);

        Assert.False(prompt.ForExchange);
        Assert.Equal(5, prompt.TargetTileX);
    }

    [Fact]
    public void EnterDoesNothingWhileTheFieldIsEmpty()
    {
        var prompt = new GoldAmountControl();
        var confirms = 0;
        prompt.OnConfirm += _ => confirms++;
        prompt.Show();

        var e = new KeyDownEvent { Key = Keys.Enter };
        prompt.OnKeyDown(e);

        Assert.True(e.Handled);
        Assert.True(prompt.Visible);
        Assert.Equal(0, confirms);
    }

    [Fact]
    public void AltEnterDoesNotConfirm()
    {
        var prompt = new GoldAmountControl();
        var confirms = 0;
        prompt.OnConfirm += _ => confirms++;
        prompt.Show();
        Type(prompt.AmountTextBox, "5");

        prompt.OnKeyDown(new KeyDownEvent { Key = Keys.Enter, Modifiers = KeyModifiers.Alt });

        Assert.True(prompt.Visible);
        Assert.Equal(0, confirms);
    }

    [Fact]
    public void EscapeAndCancelCloseOnce()
    {
        var prompt = new ItemAmountControl();
        var closes = 0;
        prompt.OnClose += () => closes++;

        prompt.ShowForSlot(1);
        prompt.OnKeyDown(new KeyDownEvent { Key = Keys.Escape });
        prompt.Hide();

        Assert.False(prompt.Visible);
        Assert.Equal(1, closes);

        prompt.ShowForSlot(1);
        Type(prompt.AmountTextBox, "9");
        prompt.CancelButton.OnClick(new ClickEvent { Button = MouseButton.Left });

        Assert.False(prompt.Visible);
        Assert.Equal(2, closes);
    }

    [Fact]
    public void ShowClearsTheFieldAndCentresInTheViewport()
    {
        var prompt = new ItemAmountControl();
        prompt.ShowForSlot(1);
        Type(prompt.AmountTextBox, "8");
        prompt.Hide();

        var exchange = new Rectangle(100, 50, 300, 200);
        prompt.SetViewportBounds(exchange);
        prompt.ShowForSlot(2);

        Assert.Equal(string.Empty, prompt.AmountTextBox.Text);
        Assert.Equal(exchange.X + (exchange.Width - prompt.Width) / 2, prompt.X);
        Assert.Equal(exchange.Y + (exchange.Height - prompt.Height) / 2, prompt.Y);
    }
}
