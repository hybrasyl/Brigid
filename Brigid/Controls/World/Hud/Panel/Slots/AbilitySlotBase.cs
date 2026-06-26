#region
#endregion

namespace Brigid.Controls.World.Hud.Panel.Slots;

/// <summary>
///     Slot control for skills and spells. Parses "Name (Lev:X/Y)" format into AbilityName and AbilityLevel. Adds
///     right-click event for chant editing.
/// </summary>
public abstract class AbilitySlotControl : PanelSlot
{
    /// <summary>
    ///     Parsed level string (e.g. "50/100"), or null if not present.
    /// </summary>
    public string? AbilityLevel { get; private set; }

    /// <summary>
    ///     Parsed ability name without the level suffix (e.g. "Ioc").
    /// </summary>
    public string? AbilityName { get; private set; }

    /// <summary>
    ///     Fired on right-click. Parameter is the 1-based slot number.
    /// </summary>
    public event AbilitySlotRightClickHandler? OnRightClick;

    /// <summary>
    ///     Sets the slot name and parses out ability name and level from the "Name (Lev:X/Y)" format.
    /// </summary>
    public void SetAbilityName(string? name)
    {
        SlotName = name;

        if (string.IsNullOrEmpty(name))
        {
            AbilityName = null;
            AbilityLevel = null;

            return;
        }

        var levIndex = name.LastIndexOf("(Lev:", StringComparison.Ordinal);

        if (levIndex > 0)
        {
            AbilityName = name[..levIndex].TrimEnd();

            var levStart = levIndex + 5;
            var levEnd = name.IndexOf(')', levStart);

            AbilityLevel = levEnd > levStart ? name[levStart..levEnd] : null;
        } else
        {
            AbilityName = name;
            AbilityLevel = null;
        }
    }

    public override void OnClick(ClickEvent e)
    {
        if ((e.Button == MouseButton.Right) && NormalTexture is not null)
        {
            OnRightClick?.Invoke(Slot);
            e.Handled = true;

            return;
        }

        base.OnClick(e);
    }
}