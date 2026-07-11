#region
using DALib.Networking.Packets.Server;
#endregion

namespace Brigid.Networking.Definitions;

/// <summary>
///     Hybrasyl protocol extensions to <see cref="SystemMessageType" /> (the 0x0A render-channel byte).
///     DALib models the retail/USDA wire format as ground truth and deliberately carries no server-specific
///     divergence; its protocol enums are byte-backed and non-validating, so extension values round-trip as
///     unnamed members and are declared here as typed constants instead. Hybrasyl-assigned values must stay
///     clear of the retail block (currently ≤ 0x12).
/// </summary>
public static class HybrasylMessageType
{
    /// <summary>0x20 — near-fullscreen markdown notice window (<c>MarkdownView</c>).</summary>
    public const SystemMessageType MarkdownNotice = (SystemMessageType)0x20;
}
