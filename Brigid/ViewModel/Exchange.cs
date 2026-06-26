using Chaos.DarkAges.Definitions;

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative exchange (trade) state. Only one exchange can be active at a time. Fires events on state transitions
///     for UI reconciliation.
/// </summary>
public sealed class Exchange
{
    private readonly List<ExchangeItemData?> MyItems = [];
    private readonly List<ExchangeItemData?> OtherItems = [];

    /// <summary>
    ///     Whether an exchange is currently active.
    /// </summary>
    public bool IsActive { get; private set; }

    /// <summary>
    ///     Whether the other player has accepted.
    /// </summary>
    public bool IsOtherAccepted { get; private set; }

    /// <summary>
    ///     My gold amount in the exchange.
    /// </summary>
    public int MyGold { get; private set; }

    /// <summary>
    ///     The other player's gold amount in the exchange.
    /// </summary>
    public int OtherGold { get; private set; }

    /// <summary>
    ///     The other player's entity ID.
    /// </summary>
    public uint OtherUserId { get; private set; }

    /// <summary>
    ///     The other player's name.
    /// </summary>
    public string OtherUserName { get; private set; } = string.Empty;

    public void AddItem(
        bool rightSide,
        byte exchangeIndex,
        ushort sprite,
        DisplayColor color,
        string? name)
    {
        //legacy DA client dead-stores the slot byte and renders in arrival
        //order using the list widget's own item count. Match that so we
        //render identically against Hybrasyl (0-based wire) and retail
        //(1-based wire).
        var items = rightSide ? OtherItems : MyItems;
        var index = (byte)items.Count;
        items.Add(new ExchangeItemData(sprite, color, name));
        ItemAdded?.Invoke(rightSide, index);
    }

    /// <summary>
    ///     Fired when the server requests a stackable item count from a specific inventory slot.
    /// </summary>
    public event ExchangeAmountRequestedHandler? AmountRequested;

    public void Close(string? message = null)
    {
        IsActive = false;
        OtherUserId = 0;
        OtherUserName = string.Empty;
        MyGold = 0;
        OtherGold = 0;
        IsOtherAccepted = false;
        MyItems.Clear();
        OtherItems.Clear();
        Closed?.Invoke(message);
    }

    /// <summary>
    ///     Fired when the exchange is closed (cancelled or completed).
    /// </summary>
    public event ExchangeClosedHandler? Closed;

    /// <summary>
    ///     Returns an item on the specified side and index, or null.
    /// </summary>
    public ExchangeItemData? GetItem(bool rightSide, byte index)
    {
        var items = rightSide ? OtherItems : MyItems;

        return index < items.Count ? items[index] : null;
    }

    /// <summary>
    ///     Returns the number of item slots on the specified side.
    /// </summary>
    public int GetItemCount(bool rightSide) => rightSide ? OtherItems.Count : MyItems.Count;

    /// <summary>
    ///     Fired when gold is set on either side. Argument is true for the other player's side.
    /// </summary>
    public event ExchangeGoldSetHandler? GoldSet;

    /// <summary>
    ///     Fired when an item is added to either side. Argument is true for the other player's side.
    /// </summary>
    public event ExchangeItemAddedHandler? ItemAdded;

    /// <summary>
    ///     Fired when the other player accepts the exchange.
    /// </summary>
    public event ExchangeOtherAcceptedHandler? OtherAccepted;

    public void RequestAmount(byte fromSlot) => AmountRequested?.Invoke(fromSlot);

    public void SetGold(bool rightSide, int amount)
    {
        if (rightSide)
            OtherGold = amount;
        else
            MyGold = amount;

        GoldSet?.Invoke(rightSide);
    }

    public void SetOtherAccepted()
    {
        IsOtherAccepted = true;
        OtherAccepted?.Invoke();
    }

    public void Start(uint otherUserId, string otherUserName)
    {
        IsActive = true;
        OtherUserId = otherUserId;
        OtherUserName = otherUserName;
        MyGold = 0;
        OtherGold = 0;
        IsOtherAccepted = false;
        MyItems.Clear();
        OtherItems.Clear();
        Started?.Invoke();
    }

    /// <summary>
    ///     Fired when a new exchange is started.
    /// </summary>
    public event ExchangeStartedHandler? Started;

    public readonly record struct ExchangeItemData(ushort Sprite, DisplayColor Color, string? Name);
}