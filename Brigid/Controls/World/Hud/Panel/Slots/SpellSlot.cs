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

    public SpellSlot() => SetChants(null);

    /// <summary>
    ///     Replaces the ten chant lines: each line that <paramref name="chants" /> does not give is empty. A spell with no
    ///     saved chants (null) then has no chant, and does not keep the lines of the last spell or of an older file.
    /// </summary>
    public void SetChants(string[]? chants) => CopyChants(chants, Chants);

    internal static void CopyChants(string[]? from, string[] to)
    {
        Array.Fill(to, string.Empty);

        if (from is not null)
            Array.Copy(from, to, Math.Min(from.Length, to.Length));
    }
}