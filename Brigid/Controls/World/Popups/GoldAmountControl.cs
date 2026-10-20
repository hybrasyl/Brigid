namespace Brigid.Controls.World.Popups;

/// <summary>
///     The gold amount prompt (BRIG-122). Shown when the player drops gold on the ground or on an entity, or puts gold in
///     an exchange. It keeps the target, so that the caller only shows it and handles <see cref="AmountPromptPanel.OnConfirm" />.
/// </summary>
public sealed class GoldAmountControl : AmountPromptPanel
{
    private const int MAX_DIGITS = 10;

    /// <summary>The entity to drop the gold on, or null to drop it on the ground.</summary>
    public uint? TargetEntityId { get; private set; }

    public int TargetTileX { get; private set; }
    public int TargetTileY { get; private set; }

    public GoldAmountControl()
        : base("goldamount", "Gold", "How much gold?", MAX_DIGITS) { }

    /// <summary>Shows the prompt for a drop on <paramref name="entityId" />, or on the tile when the entity is null.</summary>
    public void ShowForTarget(uint? entityId, int tileX, int tileY)
    {
        TargetEntityId = entityId;
        TargetTileX = tileX;
        TargetTileY = tileY;
        Show();
    }
}
