#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     Base class for panels that support an expanded mode with a taller background drawn upward from the panel's anchor
///     position. Provides shared expand state, background swapping, and the expanded draw pass. Subclasses override
///     <see cref="SetExpanded" /> to add type-specific behavior (slot visibility, text area resize).
/// </summary>
public abstract class ExpandablePanel : UIPanel
{
    private Texture2D? ExpandedBackground;

    /// <summary>
    ///     The Y offset (in pixels) between the expanded and normal backgrounds. When expanded, the panel's Y is shifted
    ///     upward by this amount and Height grows by the same amount so that hit-testing covers the expanded area.
    /// </summary>
    public int ExpandYOffset { get; private set; }

    /// <summary>
    ///     Whether expand support has been configured via <see cref="ConfigureExpand" />.
    /// </summary>
    public bool CanExpand => ExpandedBackground is not null;

    /// <summary>
    ///     Whether the panel is in expanded mode.
    /// </summary>
    public bool IsExpanded { get; private set; }

    /// <summary>
    ///     Configures expand support. Call once during construction to enable <see cref="SetExpanded" />.
    /// </summary>
    public void ConfigureExpand(Texture2D? expandedBackground)
    {
        ExpandedBackground = expandedBackground;

        var normalHeight = Background?.Height ?? Height;
        var expandedHeight = expandedBackground?.Height ?? normalHeight;
        ExpandYOffset = expandedHeight - normalHeight;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        if (IsExpanded && ExpandedBackground is not null)
        {
            DrawTexture(
                spriteBatch,
                ExpandedBackground,
                new Vector2(ScreenX, ScreenY),
                Color.White);

            foreach (var child in Children)
                if (child.Visible)
                {
                    child.Draw(spriteBatch);
                    DebugOverlay.DrawElement(spriteBatch, child);
                }

            return;
        }

        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Toggles between normal and expanded mode, adjusting the panel origin, height, and ZIndex.
    /// </summary>
    /// <remarks>
    ///     Subclasses should call <c>base.SetExpanded</c> first, then apply type-specific changes (slot visibility, text
    ///     bounds, etc.). The <see cref="IsExpanded" /> flag is always tracked even when no expanded background is set,
    ///     so composite panels (e.g. ToolsPanel) can drive their children's expand state without each child owning a
    ///     background.
    /// </remarks>
    public virtual void SetExpanded(bool expanded)
    {
        if (expanded == IsExpanded)
            return;

        IsExpanded = expanded;

        //panels without an expanded background skip the y/height/zindex adjustments — the parent
        //composite handles its own bounds, and the child only needs IsExpanded flipped so its
        //type-specific overrides (e.g. PanelBase slot visibility) run.
        if (ExpandedBackground is null)
            return;

        ZIndex = expanded ? 10 : 0;

        //adjust panel origin and height so hit-testing covers the expanded area.
        //children keep their original y values — the panel origin shift positions them correctly.
        var yShift = expanded ? -ExpandYOffset : ExpandYOffset;
        Y += yShift;
        Height -= yShift;
    }
}