namespace Brigid.Networking;

/// <summary>
///     USDA stores the stage-4 (0x2E) recruit-box class caps with Rogue and Monk transposed.
/// </summary>
/// <remarks>
///     <para>
///         The retail client sends the five cap bytes as Warrior, Wizard, Rogue, Priest, Monk —
///         confirmed at rung 1 by <c>ui_group_ad_dialog_read_model</c>, which fills wire byte
///         <c>k</c> from the dialog control of screen row <c>k</c>, and by the shipped artwork's
///         row order. USDA reads them as Warrior, Wizard, <strong>Monk</strong>, Priest,
///         <strong>Rogue</strong>, then replies on 0x63 in the correct Rogue-third order. Its two
///         directions disagree with each other, so every recruit box it has ever stored has those
///         two caps swapped.
///     </para>
///     <para>
///         This is behavioural, not cosmetic: the join gate follows the stored value. Measured
///         2026-08-07 against USDA — sending <c>1 1 1 1 5</c> (monk 5) admitted rogues in numbers
///         and echoed <c>1 1 5 1 1</c>; sending <c>1 1 5 1 1</c> (rogue 5) admitted monks and
///         echoed <c>1 1 1 1 5</c>. Both halves confirmed on the wire, and the retail client
///         displays the same swap, so this is USDA's defect rather than a client one.
///     </para>
///     <para>
///         Pre-swapping on send makes USDA hold what the player actually asked for. Its reply is
///         then correct, so no matching change is needed on receive, and the box renders correctly
///         in every client — retail included — rather than only in Brigid. Boxes created by other
///         clients remain stored swapped and are displayed exactly as USDA reports them, which is
///         what they really gate.
///     </para>
///     <para>
///         Hybrasyl reads the caps correctly, so this is gated on
///         <see cref="GlobalSettings.IsCursed" /> and must never apply off retail.
///     </para>
/// </remarks>
internal static class RetailGroupBoxQuirk
{
    /// <summary>
    ///     Maps the player's intended Rogue and Monk caps onto the byte positions the target
    ///     server will read them from.
    /// </summary>
    /// <param name="rogue">The cap the player entered on the Rogue row.</param>
    /// <param name="monk">The cap the player entered on the Monk row.</param>
    /// <param name="isRetail">
    ///     <see cref="GlobalSettings.IsCursed" />. When false the caps pass through untouched.
    /// </param>
    /// <returns>
    ///     The values to place in the packet's <c>maxRogue</c> and <c>maxMonk</c> fields — which
    ///     are wire bytes 2 and 4 of the stage-4 body.
    /// </returns>
    public static (byte MaxRogueField, byte MaxMonkField) CapsForWire(byte rogue, byte monk, bool isRetail) =>
        isRetail ? (monk, rogue) : (rogue, monk);
}
