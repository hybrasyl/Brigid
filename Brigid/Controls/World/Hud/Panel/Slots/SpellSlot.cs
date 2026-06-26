#region
using Chaos.DarkAges.Definitions;
#endregion

namespace Brigid.Controls.World.Hud.Panel.Slots;

/// <summary>
///     Spell slot with 10 chant lines, spell type, and optional prompt.
/// </summary>
public sealed class SpellSlot : AbilitySlotControl
{
    public byte CastLines { get; set; }
    public string Prompt { get; set; } = string.Empty;
    public SpellType SpellType { get; set; }
    public string[] Chants { get; } = new string[10];

    public SpellSlot()
    {
        for (var i = 0; i < 10; i++)
            Chants[i] = string.Empty;
    }
}