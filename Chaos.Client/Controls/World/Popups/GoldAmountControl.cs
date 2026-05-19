#region
using Chaos.Client.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Gold drop/exchange popup using _nmoney prefab. Stores the pending drop target (entity or tile)
///     so the caller only needs to show it and subscribe to OnConfirm.
/// </summary>
public sealed class GoldAmountControl : PrefabPanel
{
    /// <summary>
    ///     Entity ID to drop gold on, or null for ground drop.
    /// </summary>
    public uint? TargetEntityId { get; private set; }

    public int TargetTileX { get; private set; }
    public int TargetTileY { get; private set; }
    public UITextBox? AmountTextBox { get; }
    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public GoldAmountControl()
        : base("_nmoney")
    {
        Name = "GoldExchange";
        Visible = false;
        UsesControlStack = true;

        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");
        AmountTextBox = CreateTextBox("Text", 10);

        //replace any existing text display with a label showing the prompt
        var titleLabel = CreateLabel("Title");

        titleLabel?.ForegroundColor = Color.White;
        titleLabel?.Text = "Gold amount to drop?";

        if (OkButton is not null)
            OkButton.Clicked += Confirm;

        if (CancelButton is not null)
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
    ///     Fired when the user confirms a gold amount. Parameter is the parsed amount.
    /// </summary>
    public event AmountConfirmedHandler? OnConfirm;

    /// <summary>
    ///     Fired after the popup transitions from visible to hidden via any close path (OK, Cancel, ESC).
    ///     Used to clear ambient state set while the popup was open (e.g. the HUD description bar).
    /// </summary>
    public event Action? Closed;

    public override void Hide()
    {
        var wasVisible = Visible;

        base.Hide();

        if (wasVisible)
            Closed?.Invoke();
    }

    public override void Show()
    {
        if (AmountTextBox is not null)
        {
            AmountTextBox.Text = string.Empty;
            AmountTextBox.IsFocused = true;
        }

        base.Show();
    }

    public void ShowForTarget(uint? entityId, int tileX, int tileY)
    {
        TargetEntityId = entityId;
        TargetTileX = tileX;
        TargetTileY = tileY;
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