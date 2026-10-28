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
    ///     Whether the player cancelled and the client waits for the server's answer (BRIG-168). The server's Cancelled
    ///     event, or the Accepted event that completes the trade, closes the exchange.
    /// </summary>
    public bool IsCancelPending { get; private set; }

    /// <summary>
    ///     Whether the server confirmed that you accepted.
    /// </summary>
    public bool IsMyAccepted { get; private set; }

    /// <summary>
    ///     Whether the server confirmed that the other player accepted.
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
        IsMyAccepted = false;
        IsOtherAccepted = false;
        IsCancelPending = false;
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
    ///     Fired when the server confirms that one side accepted. Argument is true for the other player's side.
    /// </summary>
    public event ExchangeAcceptedHandler? Accepted;

    public void RequestAmount(byte fromSlot) => AmountRequested?.Invoke(fromSlot);

    /// <summary>
    ///     Records that the player cancelled, and returns true: the client sends the Cancel request. As on retail, the
    ///     exchange stays open until the server answers. Returns false, and the client sends nothing, when no exchange is
    ///     active or a cancel already waits for the server.
    /// </summary>
    public bool RequestCancel()
    {
        if (!IsActive || IsCancelPending)
            return false;

        IsCancelPending = true;

        return true;
    }

    public void SetGold(bool rightSide, int amount)
    {
        if (rightSide)
            OtherGold = amount;
        else
            MyGold = amount;

        GoldSet?.Invoke(rightSide);
    }

    /// <summary>
    ///     Records the server's confirmation that one side accepted (BRIG-140), and closes the exchange with
    ///     <paramref name="message" /> when both sides have accepted, as the retail client does. Retail confirms your side
    ///     to you when you accept, and the other side when the other player accepts; Hybrasyl confirms the other side on
    ///     each accept, then your side to both players when both have accepted. The rule gives the same result for both.
    ///     Does nothing when no exchange is active, so that a late confirmation does not close the exchange again.
    /// </summary>
    public void SetAccepted(bool rightSide, string? message)
    {
        if (!IsActive)
            return;

        if (rightSide)
            IsOtherAccepted = true;
        else
            IsMyAccepted = true;

        Accepted?.Invoke(rightSide);

        if (IsMyAccepted && IsOtherAccepted)
            Close(message);
    }

    public void Start(uint otherUserId, string otherUserName)
    {
        IsActive = true;
        OtherUserId = otherUserId;
        OtherUserName = otherUserName;
        MyGold = 0;
        OtherGold = 0;
        IsMyAccepted = false;
        IsOtherAccepted = false;
        IsCancelPending = false;
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