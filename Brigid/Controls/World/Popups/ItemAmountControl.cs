namespace Brigid.Controls.World.Popups;

/// <summary>
///     The item amount prompt (BRIG-122). Shown when the player puts a stackable item in an exchange: the server asks for a
///     quantity, and this prompt collects it. The world screen centres it on the exchange window.
/// </summary>
public sealed class ItemAmountControl : AmountPromptPanel
{
    private const int MAX_DIGITS = 3;

    /// <summary>The 1-based inventory slot of the stackable item in the exchange.</summary>
    public byte ItemSlot { get; private set; }

    public ItemAmountControl()
        : base("itemamount", "Give Items", "How many will you give?", MAX_DIGITS) { }

    /// <summary>Shows the prompt for the item in <paramref name="slot" />.</summary>
    public void ShowForSlot(byte slot)
    {
        ItemSlot = slot;
        Show();
    }
}
