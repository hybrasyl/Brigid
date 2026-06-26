#region
using Brigid.Controls.Components;
using Brigid.Data.Models;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     World abilities panel (H key). Displays page 3 of the skill book on the left half and page 3 of the spell
///     book on the right half. Composite of two <see cref="PanelBase" /> children — each handles its own data
///     subscription, drag, hover, and cooldown polling. The parent draws the shared background and forwards
///     expand state.
/// </summary>
public sealed class ToolsPanel : ExpandablePanel
{
    private const int CELL_WIDTH = 36;
    private const int LEFT_GRID_X = 8;
    private const int RIGHT_GRID_X = LEFT_GRID_X + 6 * CELL_WIDTH;
    private const int CELL_COUNT_PER_HALF = 18;

    /// <summary>
    ///     The skill page-3 (world abilities) sub-panel. Property name parallels the <see cref="UIElement.Name" /> of
    ///     "SkillBookWorld" set by <see cref="SkillBookPanel" /> for this page.
    /// </summary>
    public SkillBookPanel WorldSkills { get; }

    /// <summary>
    ///     The spell page-3 (world abilities) sub-panel. Property name parallels the <see cref="UIElement.Name" /> of
    ///     "SpellBookWorld" set by <see cref="SpellBookPanel" /> for this page.
    /// </summary>
    public SpellBookPanel WorldSpells { get; }

    public ToolsPanel(
        ControlPrefabSet hudPrefabSet,
        Texture2D? background = null,
        int normalVisibleSlots = CELL_COUNT_PER_HALF)
    {
        Name = "Tools";

        if (background is not null)
        {
            Background = background;
            Width = background.Width;
            Height = background.Height;
        }

        //sub-panels have no background of their own, so panelbase's auto-detect for compactgridpadding
        //(which requires Background is not null) returns 0. the parent composite must pass the right
        //value explicitly: the large hud's compact 1-row LivingInventoryBackground needs +4 above the
        //slot grid; the small hud's full 3-row background does not. the discriminator is the parent's
        //own visible-slots-per-half count — fewer than 18 per half means the parent is in compact mode.
        //passing compactGridPadding also ensures the +4/-4 shift on expand runs correctly.
        var childCompactPadding = normalVisibleSlots < CELL_COUNT_PER_HALF ? 4 : 0;

        //children use gridOffsetX: 0 so slots start at child-local x = 0. the children are then positioned
        //at LEFT_GRID_X / RIGHT_GRID_X and their Width is clamped to a single half's grid span (6 columns).
        //non-overlapping bounds make InputDispatcher.HitTest route each half's clicks unambiguously to the
        //correct child — the previous scheme (both children sized to the parent's full width, slots offset
        //by gridOffsetX) caused WorldSpells to swallow every click because it iterates last in z-order and
        //its ContainsPoint accepted coordinates inside the left half.
        WorldSkills = new SkillBookPanel(
            hudPrefabSet,
            SkillBookPage.Page3,
            null,
            normalVisibleSlots,
            6,
            CELL_COUNT_PER_HALF,
            0,
            false,
            false,
            childCompactPadding);

        WorldSpells = new SpellBookPanel(
            hudPrefabSet,
            SkillBookPage.Page3,
            null,
            normalVisibleSlots,
            6,
            CELL_COUNT_PER_HALF,
            0,
            false,
            false,
            childCompactPadding);

        const int HALF_WIDTH = 6 * CELL_WIDTH;

        WorldSkills.X = LEFT_GRID_X;
        WorldSkills.Width = HALF_WIDTH;
        WorldSkills.Height = Height;

        WorldSpells.X = RIGHT_GRID_X;
        WorldSpells.Width = HALF_WIDTH;
        WorldSpells.Height = Height;

        AddChild(WorldSkills);
        AddChild(WorldSpells);
    }

    /// <summary>
    ///     Configures expand support for the H tab composite. The expanded background is shared and drawn by this
    ///     parent panel; each child receives a per-half expanded visible-slot count for the slot-visibility flip in
    ///     <see cref="PanelBase.SetExpanded" />. Named to disambiguate from the inherited
    ///     <see cref="ExpandablePanel.ConfigureExpand(Texture2D?)" /> single-arg overload.
    /// </summary>
    public void ConfigureExpandPerHalf(Texture2D? expandedBackground, int expandedVisibleSlotsPerHalf)
    {
        ConfigureExpand(expandedBackground);

        WorldSkills.ConfigureExpand(null, expandedVisibleSlotsPerHalf);
        WorldSpells.ConfigureExpand(null, expandedVisibleSlotsPerHalf);

        //grow the children to the expanded extent so hit-testing covers expanded slot rows.
        //the children's local origin shifts up with the parent in expanded mode (because
        //ScreenY = parent.ScreenY + child.Y), so a height matching the expanded background
        //covers the full row range in both states.
        if (expandedBackground is not null)
        {
            WorldSkills.Height = expandedBackground.Height;
            WorldSpells.Height = expandedBackground.Height;
        }
    }

    public override void SetExpanded(bool expanded)
    {
        if (expanded == IsExpanded)
            return;

        base.SetExpanded(expanded);

        WorldSkills.SetExpanded(expanded);
        WorldSpells.SetExpanded(expanded);
    }

    //note: WorldSkills and WorldSpells are added via AddChild, so UIPanel.Dispose() walks the
    //Children list and disposes them transitively. Do not dispose them again here.
}
