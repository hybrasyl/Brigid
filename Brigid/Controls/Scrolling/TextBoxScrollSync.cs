#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Rendering;
#endregion

namespace Brigid.Controls.Scrolling;

/// <summary>
///     Shared scrollbar↔editable-textbox glue for panels that pair a <see cref="UITextBox" /> body with an
///     art-anchored <see cref="ScrollBarControl" /> (the board/mail send editors). Deliberately not a
///     <see cref="ScrollModel" /> binding: the textbox mutates its own <see cref="UITextBox.ScrollOffset" />
///     internally (caret tracking, wheel) with no change event, so the sync must poll — and a per-frame
///     <see cref="ScrollModel.Configure" /> would re-fire metrics events every frame for no gain over this
///     direct push.
/// </summary>
public static class TextBoxScrollSync
{
    /// <summary>Routes bar interaction (arrows / thumb / track) into the textbox's pixel scroll offset.</summary>
    public static void Wire(UITextBox box, ScrollBarControl bar)
        => bar.OnValueChanged += v => box.ScrollOffset = v * TextRenderer.CHAR_HEIGHT;

    /// <summary>
    ///     Per-frame push of the textbox's line metrics onto the bar. Content can shrink while scrolled down
    ///     (e.g. deleting near the bottom) — the offset is pulled back in range, otherwise the bar disables
    ///     itself while early lines are stranded out of reach above the viewport.
    /// </summary>
    public static void Sync(UITextBox box, ScrollBarControl bar)
    {
        bar.TotalItems = box.LineCount;
        bar.VisibleItems = box.VisibleLineCount;
        bar.MaxValue = Math.Max(0, box.LineCount - box.VisibleLineCount);

        box.ScrollOffset = Math.Min(box.ScrollOffset, bar.MaxValue * TextRenderer.CHAR_HEIGHT);
        bar.Value = Math.Clamp(box.ScrollOffset / TextRenderer.CHAR_HEIGHT, 0, bar.MaxValue);
    }
}
