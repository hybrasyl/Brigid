#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Item amount popup using litemex prefab. Shown when adding a stackable item to an exchange —
///     the server asks for a quantity and this control collects it from the player.
/// </summary>
public sealed class ItemAmountControl : PrefabPanel
{
    //butt001.epf frame indices (3 per button: normal, pressed, disabled)
    private const int OK_NORMAL = 15;
    private const int OK_PRESSED = 16;
    private const int OK_DISABLED = 17;
    private const int CANCEL_NORMAL = 21;
    private const int CANCEL_PRESSED = 22;

    /// <summary>
    ///     The 1-based inventory slot of the stackable item being exchanged.
    /// </summary>
    public byte ItemSlot { get; private set; }

    public UITextBox? AmountTextBox { get; }
    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public ItemAmountControl()
        : base("litemex")
    {
        Name = "ItemAmount";
        Visible = false;
        UsesControlStack = true;

        var cache = UiRenderer.Instance!;

        //ok button — positioned from prefab rect, textured from butt001.epf
        var okRect = GetRect("OK");
        OkButton = new UIButton
        {
            Name = "OK",
            X = okRect.X,
            Y = okRect.Y,
            Width = okRect.Width,
            Height = okRect.Height,
            NormalTexture = cache.GetEpfTexture("butt001.epf", OK_NORMAL),
            PressedTexture = cache.GetEpfTexture("butt001.epf", OK_PRESSED),
            DisabledTexture = cache.GetEpfTexture("butt001.epf", OK_DISABLED),
            Enabled = false
        };
        AddChild(OkButton);

        //cancel button
        var cancelRect = GetRect("Cancel");
        CancelButton = new UIButton
        {
            Name = "Cancel",
            X = cancelRect.X,
            Y = cancelRect.Y,
            Width = cancelRect.Width,
            Height = cancelRect.Height,
            NormalTexture = cache.GetEpfTexture("butt001.epf", CANCEL_NORMAL),
            PressedTexture = cache.GetEpfTexture("butt001.epf", CANCEL_PRESSED)
        };
        AddChild(CancelButton);

        AmountTextBox = CreateTextBox("Text", 3);

        var titleLabel = CreateLabel("Title");
        titleLabel?.ForegroundColor = Color.White;
        titleLabel?.Text = "How many will you give?";

        OkButton.Clicked += Confirm;
        CancelButton.Clicked += Cancel;
    }

    private void Cancel() => Hide();

    private void Confirm()
    {
        var text = AmountTextBox?.Text ?? string.Empty;

        Hide();

        if (!uint.TryParse(text, out var amount) || (amount == 0))
            return;

        OnConfirm?.Invoke(amount);
    }

    /// <summary>
    ///     Fired when the user confirms an item amount. Parameter is the parsed amount.
    /// </summary>
    public event AmountConfirmedHandler? OnConfirm;

    public override void Update(GameTime gameTime)
    {
        if (!Visible)
            return;

        base.Update(gameTime);

        OkButton?.Enabled = !string.IsNullOrEmpty(AmountTextBox?.Text);
    }

    public void ShowForSlot(byte slot)
    {
        ItemSlot = slot;

        if (AmountTextBox is not null)
        {
            AmountTextBox.Text = string.Empty;
            AmountTextBox.IsFocused = true;
        }

        OkButton?.Enabled = false;

        Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                Hide();
                e.Handled = true;

                break;

            case Keys.Enter:
                Confirm();
                e.Handled = true;

                break;
        }
    }
}