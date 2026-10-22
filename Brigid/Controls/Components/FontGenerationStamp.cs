#region
using Brigid.Rendering;
#endregion

namespace Brigid.Controls.Components;

/// <summary>
///     The <see cref="FontEngine.Generation" /> at which a control last measured, wrapped or fitted its text. A new face or
///     a new window scale bumps the generation, and then <see cref="Changed" /> is true until the control lays its text
///     out again and calls <see cref="Mark" />. A new stamp is changed, so the first check lays the text out.
/// </summary>
public sealed class FontGenerationStamp
{
    private int Generation = -1;

    /// <summary>True when the text layout is from another generation than the current one.</summary>
    public bool Changed => Generation != FontEngine.Instance.Generation;

    /// <summary>Records that the text layout is from the current generation.</summary>
    public void Mark() => Generation = FontEngine.Instance.Generation;
}
