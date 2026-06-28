#region
using Chaos.Networking.Entities.Server;
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.ViewModel;

/// <summary>
///     Authoritative NPC dialog/menu interaction state. Only one interaction can be active at a time. Fires events when
///     the server sends dialog or menu display commands.
/// </summary>
public sealed class NpcInteraction
{
    /// <summary>
    ///     The current dialog args, or null if no dialog is active.
    /// </summary>
    public NpcDialogPacket? CurrentDialog { get; private set; }

    /// <summary>
    ///     The current menu args, or null if no menu is active.
    /// </summary>
    public NpcMenuPacket? CurrentMenu { get; private set; }

    /// <summary>
    ///     Whether a dialog or menu is currently active.
    /// </summary>
    public bool IsActive => CurrentDialog is not null || CurrentMenu is not null;

    public void Close()
    {
        CurrentDialog = null;
        CurrentMenu = null;
        DialogChanged?.Invoke();
    }

    /// <summary>
    ///     Fired when a new dialog is displayed (or CloseDialog received).
    /// </summary>
    public event DialogChangedHandler? DialogChanged;

    /// <summary>
    ///     Fired when a new menu is displayed.
    /// </summary>
    public event MenuChangedHandler? MenuChanged;

    public void ShowDialog(NpcDialogPacket args)
    {
        CurrentMenu = null;
        CurrentDialog = args;
        DialogChanged?.Invoke();
    }

    public void ShowMenu(NpcMenuPacket args)
    {
        CurrentDialog = null;
        CurrentMenu = args;
        MenuChanged?.Invoke();
    }
}