using Brigid.Controls.World.Hud.Panel.Slots;

namespace Brigid.Models;

public sealed class SlotDragPayload
{
    public required PanelSlot Source { get; init; }
    public required byte SlotIndex { get; init; }
    public required HudTab SourcePanel { get; init; }
}