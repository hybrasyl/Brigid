#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Data.Models;
using Brigid.Systems;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The <see cref="CastablePopupControl" /> Details tab: icon + name/level header, the SClass metadata
///     requirement rows (level/ability/master, the five stats, up to two prerequisites), a live slot-state line,
///     and the scrollable description. Every requirement is coloured through <see cref="AbilityRequirements" />, so
///     it reads identically to the status-book detail popup. A castable with no metadata (server-only, or a class
///     with no meta file) still renders the live-state section.
/// </summary>
internal sealed class CastableDetailsPane : UIPanel
{
    private const int ICON_SIZE = 32;
    private const int ROW_H = 14;
    private const int ROW_GAP = 2;
    private const int ICON_TO_TEXT = 8;
    private const int HEADER_ROWS = 3;
    private const int STAT_COUNT = 5;

    private static readonly string[] StatNames = ["Str", "Int", "Wis", "Con", "Dex"];

    private readonly UIImage Icon;
    private readonly UILabel Header;
    private readonly UILabel Requirement;
    private readonly UILabel Live;
    private readonly UILabel[] Stats = new UILabel[STAT_COUNT];
    private readonly UILabel PreReq1;
    private readonly UILabel PreReq2;
    private readonly UILabel SpellState;
    private readonly SelectableTextView Description;

    private byte BoundSlot;
    private bool BoundIsSpell;

    public CastableDetailsPane(Rectangle pane)
    {
        X = pane.X;
        Y = pane.Y;
        Width = pane.Width;
        Height = pane.Height;

        Icon = new UIImage
        {
            X = 0,
            Y = 0,
            Width = ICON_SIZE,
            Height = ICON_SIZE,
            IsHitTestVisible = false
        };
        AddChild(Icon);

        var textX = ICON_SIZE + ICON_TO_TEXT;
        var textW = pane.Width - textX;

        Header = AddRow(textX, 0, textW);
        Requirement = AddRow(textX, ROW_H, textW);
        Live = AddRow(textX, 2 * ROW_H, textW);

        //clear both the icon and the three header rows beside it, whichever is taller.
        var statY = Math.Max(ICON_SIZE, HEADER_ROWS * ROW_H) + ROW_GAP;

        //one label per stat so each carries its own met/unmet colour.
        var statW = pane.Width / STAT_COUNT;

        for (var i = 0; i < STAT_COUNT; i++)
            Stats[i] = AddRow(i * statW, statY, statW);

        PreReq1 = AddRow(0, statY + ROW_H + ROW_GAP, pane.Width);
        PreReq2 = AddRow(0, statY + 2 * ROW_H + ROW_GAP, pane.Width);
        SpellState = AddRow(0, statY + 3 * ROW_H + ROW_GAP, pane.Width);

        var descTop = statY + 4 * ROW_H + ROW_GAP * 3;

        Description = new SelectableTextView(new Rectangle(0, descTop, pane.Width, pane.Height - descTop));
        AddChild(Description);
    }

    private UILabel AddRow(int x, int y, int width)
    {
        var label = new UILabel
        {
            X = x,
            Y = y,
            Width = width,
            Height = ROW_H,
            PaddingLeft = 0,
            PaddingTop = 0,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
            ForegroundColor = LegendColors.White,
            IsHitTestVisible = false
        };

        AddChild(label);

        return label;
    }

    /// <summary>Populates the tab for a castable slot. Safe to call whether or not metadata exists for it.</summary>
    public void Bind(
        byte slot,
        string name,
        string level,
        Texture2D? icon,
        bool isSpell)
    {
        BoundSlot = slot;
        BoundIsSpell = isSpell;

        AbilityMetadataEntry? entry = null;
        WorldState.AbilityMetadata?.TryGet(name, isSpell, out entry);

        //the metadata icon carries the learnable/locked duotone treatment; without metadata, fall back to the
        //slot's own texture (always present — right-click is gated on it).
        if (entry is not null)
        {
            var resolved = AbilityRequirements.ResolveIcon(entry);
            Icon.Texture = resolved.Texture;
            Icon.TextureOffset = new Vector2(resolved.OffsetX, resolved.OffsetY);
        } else
        {
            Icon.Texture = icon;
            Icon.TextureOffset = Vector2.Zero;
        }

        Header.Text = string.IsNullOrEmpty(level) ? name : $"{name}  (Lev {level})";
        SpellState.Text = isSpell ? FormatSpellState(slot) : string.Empty;
        RefreshLiveState();

        if (entry is null)
        {
            Requirement.Text = string.Empty;
            PreReq1.Text = string.Empty;
            PreReq2.Text = string.Empty;

            foreach (var label in Stats)
                label.Text = string.Empty;

            Description.SetText("No metadata available for this ability.", DialogPalette.DisabledText);

            return;
        }

        var attrs = WorldState.Attributes.Current;

        if (entry.RequiresMaster)
        {
            Requirement.Text = "Requires: master";
            Requirement.ForegroundColor = AbilityRequirements.RequirementColor(WorldState.IsMaster);
        } else if (entry.AbilityLevel > 0)
        {
            Requirement.Text = $"Requires: ability {entry.AbilityLevel}";
            Requirement.ForegroundColor = AbilityRequirements.RequirementColor(entry.AbilityLevel, attrs?.Ability);
        } else
        {
            Requirement.Text = $"Requires: level {entry.Level}";
            Requirement.ForegroundColor = AbilityRequirements.RequirementColor(entry.Level, attrs?.Level);
        }

        BindStat(0, entry.Str, attrs?.Str);
        BindStat(1, entry.Int, attrs?.Int);
        BindStat(2, entry.Wis, attrs?.Wis);
        BindStat(3, entry.Con, attrs?.Con);
        BindStat(4, entry.Dex, attrs?.Dex);

        BindPreReq(PreReq1, entry.PreReq1Name, entry.PreReq1Level);
        BindPreReq(PreReq2, entry.PreReq2Name, entry.PreReq2Level);

        Description.SetText(entry.Description, TextColors.Default);
    }

    /// <summary>Drops the icon reference when the popup closes, mirroring the legacy popup's Hide.</summary>
    public void Release() => Icon.Texture = null;

    /// <summary>
    ///     Routes a key to the selectable description so Ctrl+C / select-all / caret navigation work. The popup is
    ///     the top control, so keyboard events stop there instead of descending — the same forwarding the board
    ///     read panels do for their body label.
    /// </summary>
    public void ForwardKey(KeyDownEvent e) => Description.ForwardKey(e);

    /// <summary>
    ///     The cooldown line is a ticking value, so it is re-read every frame the tab is on screen rather than
    ///     frozen at bind time.
    /// </summary>
    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        if (Visible)
            RefreshLiveState();
    }

    private void RefreshLiveState()
    {
        var remainingMs = BoundIsSpell
            ? WorldState.SpellBook.GetCooldownRemainingMs(BoundSlot)
            : WorldState.SkillBook.GetCooldownRemainingMs(BoundSlot);

        Live.Text = $"Slot {BoundSlot} — {(remainingMs > 0 ? $"cooldown {remainingMs / 1000f:0.0}s" : "ready")}";
    }

    private void BindStat(int index, byte required, int? current)
    {
        var label = Stats[index];
        label.Text = $"{StatNames[index]} {required}";
        label.ForegroundColor = AbilityRequirements.RequirementColor(required, current);
    }

    private static void BindPreReq(UILabel label, string? name, byte level)
    {
        if (name is null)
        {
            label.Text = string.Empty;

            return;
        }

        label.Text = $"Requires: {AbilityRequirements.FormatPreReq(name, level)}";
        label.ForegroundColor = AbilityRequirements.RequirementColor(AbilityRequirements.HasPreRequisite(name, level));
    }

    private static string FormatSpellState(byte slot)
    {
        ref readonly var data = ref WorldState.SpellBook.GetSlot(slot);

        if (!data.IsOccupied)
            return string.Empty;

        var text = $"{data.SpellType} — {data.CastLines} cast line{(data.CastLines == 1 ? string.Empty : "s")}";

        return string.IsNullOrEmpty(data.Prompt) ? text : $"{text} — \"{data.Prompt}\"";
    }
}
