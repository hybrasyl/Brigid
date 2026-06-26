#region
using Brigid.Controls.World.Hud.Panel.Slots;
using Brigid.Networking;
#endregion

namespace Brigid.Systems;

/// <summary>
///     Tracks the state of the spell casting pipeline: target selection (cast mode) and chant line sequencing.
/// </summary>
public sealed class CastingSystem
{
    private const float CHANT_INTERVAL_MS = 1000f;
    private float ElapsedMs;
    private int LineIndex;

    private uint ChantTargetId;
    private int ChantTargetX;
    private int ChantTargetY;

    /// <summary>
    ///     The spell currently being chanted. Null when no chant is in progress.
    /// </summary>
    public SpellSlot? ChantingSlot { get; private set; }

    /// <summary>
    ///     The spell awaiting target selection. Independent of <see cref="ChantingSlot" /> so the player can
    ///     pre-aim a new spell while an existing chant is still running; <see cref="SelectTarget" /> aborts the
    ///     old chant and starts the new one.
    /// </summary>
    public SpellSlot? TargetingSlot { get; private set; }

    /// <summary>
    ///     True when chant lines are being sent on a timer.
    /// </summary>
    public bool IsChanting => ChantingSlot is not null;

    /// <summary>
    ///     True when waiting for the player to select a target entity.
    /// </summary>
    public bool IsTargeting => TargetingSlot is not null;

    /// <summary>
    ///     True when any casting activity is in progress (targeting or chanting).
    /// </summary>
    public bool IsActive => IsTargeting || IsChanting;

    /// <summary>
    ///     Enters target selection mode for a spell. Does NOT touch any in-progress chant — the caller can
    ///     queue up the next spell's aim while the current chant keeps running.
    /// </summary>
    public void BeginTargeting(SpellSlot spellSlot) => TargetingSlot = spellSlot;

    /// <summary>
    ///     Exits target selection without starting a cast. Preserves any in-progress chant.
    /// </summary>
    public void CancelTargeting() => TargetingSlot = null;

    /// <summary>
    ///     Handles a server-sent CancelCasting — aborts an in-progress chant only. Targeting state is left
    ///     alone so movement/auto-assail side effects on the server don't kick the player out of cast mode
    ///     when they haven't selected a target yet.
    /// </summary>
    public void CancelChant()
    {
        ChantingSlot = null;
        LineIndex = 0;
        ElapsedMs = 0;
    }

    /// <summary>
    ///     Clears all casting activity — targeting and chanting alike.
    /// </summary>
    public void Reset()
    {
        CancelTargeting();
        CancelChant();
    }

    /// <summary>
    ///     Uses the currently-targeting spell on the supplied target. Aborts any in-progress chant (the new
    ///     spell always takes over) and either casts immediately (0-line spells) or begins a new chant.
    ///     Returns true if a chant was started, false if the spell was cast immediately or no-op'd.
    /// </summary>
    public bool SelectTarget(
        uint targetId,
        int targetX,
        int targetY,
        ConnectionManager connection)
    {
        var selected = TargetingSlot;
        TargetingSlot = null;

        if (selected is null)
            return false;

        //new spell overrides the current chant whether 0-line or multi-line
        CancelChant();

        if (selected.CastLines == 0)
        {
            connection.UseSpellOnTarget(
                selected.Slot,
                targetId,
                targetX,
                targetY);

            return false;
        }

        //begin new chant sequence
        ChantingSlot = selected;
        ChantTargetId = targetId;
        ChantTargetX = targetX;
        ChantTargetY = targetY;
        LineIndex = 1;
        ElapsedMs = 0;

        connection.SendBeginChant(selected.CastLines);

        var firstChant = selected.Chants[0];

        if (!string.IsNullOrEmpty(firstChant))
            connection.SendChant(firstChant);

        return true;
    }

    /// <summary>
    ///     Ticks the chant timer. Sends the next chant line every second. On the final tick, sends the spell name and cast
    ///     packet.
    /// </summary>
    public void Update(float deltaMs, ConnectionManager connection)
    {
        if (ChantingSlot is null)
            return;

        ElapsedMs += deltaMs;

        if (ElapsedMs < CHANT_INTERVAL_MS)
            return;

        ElapsedMs -= CHANT_INTERVAL_MS;

        var castLines = ChantingSlot.CastLines;

        if (LineIndex < castLines)
        {
            var chant = ChantingSlot.Chants[LineIndex];

            if (!string.IsNullOrEmpty(chant))
                connection.SendChant(chant);

            LineIndex++;
        } else
        {
            //final: send spell name as chant + cast packet
            var spellName = ChantingSlot.AbilityName ?? string.Empty;

            if (!string.IsNullOrEmpty(spellName))
                connection.SendChant(spellName);

            connection.UseSpellOnTarget(
                ChantingSlot.Slot,
                ChantTargetId,
                ChantTargetX,
                ChantTargetY);
            CancelChant();
        }
    }
}