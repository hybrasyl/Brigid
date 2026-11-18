namespace Brigid.Controls.World.Hud;

/// <summary>The three HUDs while the new HUD is built (BRIG-127).</summary>
internal enum HudLayout
{
    Classic,
    Large,
    Main
}

/// <summary>
///     Which HUD shows (BRIG-127 D42). / swaps the classic and the large HUD. Shift+/ changes to the new HUD, and back to
///     the HUD that showed before it; / does nothing while the new HUD shows. BRIG-156 removes the classic and the large
///     HUD, and this switch with them.
/// </summary>
internal sealed class HudLayoutSwitch
{
    //the HUD that the main HUD goes back to
    private HudLayout Previous = HudLayout.Classic;

    public HudLayout Active { get; private set; } = HudLayout.Classic;

    /// <summary>Swaps the classic and the large HUD. Returns false, and changes nothing, while the main HUD shows.</summary>
    public bool Swap()
    {
        if (Active == HudLayout.Main)
            return false;

        Active = Active == HudLayout.Classic ? HudLayout.Large : HudLayout.Classic;

        return true;
    }

    /// <summary>Changes to the main HUD, or from it to the HUD that showed before.</summary>
    public void ToggleMain()
    {
        if (Active == HudLayout.Main)
            Active = Previous;
        else
        {
            Previous = Active;
            Active = HudLayout.Main;
        }
    }
}
