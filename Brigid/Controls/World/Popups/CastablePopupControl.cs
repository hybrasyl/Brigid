#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Controls.World.Popups.Profile;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The right-click popup for a castable (skill or spell) slot: a from-scratch
///     <see cref="CenteredModalPanel" /> with two tabs.
///     <list type="bullet">
///         <item><b>Lines</b> — the chant-line editor that replaces the <c>lssbook</c> prefab popup. One text box per
///         cast line (skills always have one); Save (or Enter) raises <see cref="OnChantSet" />, which the host
///         persists exactly as before. Escape / Close discards.</item>
///         <item><b>Details</b> — the ability's requirements and description from the class's SClass metadata
///         (<see cref="WorldState.TryGetAbilityMetadata" />), colour-coded against the player's live stats through
///         <see cref="AbilityRequirements" />, plus live slot state (cooldown, and a spell's type / cast lines /
///         prompt). A server-only castable with no metadata still gets the live-state section.</item>
///     </list>
///     The panel is a fixed size — big enough for ten chant lines — so switching tabs never resizes it. That
///     deliberately drops the legacy popup's per-line-count height juggling and its tiled <c>MidImage</c> strip.
/// </summary>
public sealed class CastablePopupControl : CenteredModalPanel
{
    private const int PANEL_W = 320;
    private const int PANEL_H = 300;

    private const int TAB_W = 72;
    private const int TAB_H = 20;
    private const int TAB_GAP = 4;
    private const int TAB_TO_PANE = 6;

    private const int MAX_LINES = 10;
    private const int LINE_H = 20;
    private const int LINE_STRIDE = 22;
    private const int CHANT_MAX_LENGTH = 32;
    private const int SAVE_W = 52;

    private static readonly string[] TabTitles = ["Lines", "Details"];

    private readonly SelectableTab[] Tabs = new SelectableTab[2];
    private readonly UIPanel[] TabPanes = new UIPanel[2];

    private readonly UITextBox[] TextInputs = new UITextBox[MAX_LINES];
    private readonly TextButton SaveButton;
    private readonly DetailsPane Details;

    private byte EditingSlot;
    private bool IsSpell;
    private int LineCount;

    /// <summary>Raised when the chant lines are committed. Parameters: slot (1-based), lines, isSpell.</summary>
    public event ChantSetHandler? OnChantSet;

    public CastablePopupControl()
        : base("Castable", PANEL_W, PANEL_H)
    {
        Name = "CastablePopup";

        var content = ContentBounds;
        var pane = new Rectangle(content.X, content.Y + TAB_H + TAB_TO_PANE, content.Width, content.Height - TAB_H - TAB_TO_PANE);

        BuildTabRow(content);
        SaveButton = AddBottomBarButton("Save", SAVE_W, Confirm);

        TabPanes[0] = BuildLinesPane(pane);

        Details = new DetailsPane(pane);
        TabPanes[1] = Details;
        AddChild(Details);

        SelectTab(0);
    }

    private void BuildTabRow(Rectangle content)
    {
        for (var i = 0; i < Tabs.Length; i++)
        {
            var index = i;

            var tab = new SelectableTab(TabTitles[i], TAB_W, TAB_H)
            {
                X = content.X + i * (TAB_W + TAB_GAP),
                Y = content.Y
            };

            tab.Clicked += () => SelectTab(index);
            Tabs[i] = tab;
            AddChild(tab);
        }
    }

    private UIPanel BuildLinesPane(Rectangle pane)
    {
        var content = new InputSlotPane
        {
            X = pane.X,
            Y = pane.Y,
            Width = pane.Width,
            Height = pane.Height
        };

        for (var i = 0; i < MAX_LINES; i++)
        {
            TextInputs[i] = new UITextBox
            {
                Name = $"ChantLine{i}",
                X = 0,
                Y = i * LINE_STRIDE,
                Width = pane.Width,
                Height = LINE_H,
                MaxLength = CHANT_MAX_LENGTH,
                Visible = false,
                ForegroundColor = LegendColors.White,
                FocusedBackgroundColor = DialogPalette.RowHoverFill
            };

            content.AddChild(TextInputs[i]);
        }

        AddChild(content);

        return content;
    }

    // ── tabs ──

    private void SelectTab(int index)
    {
        for (var i = 0; i < Tabs.Length; i++)
        {
            Tabs[i].IsSelected = i == index;
            TabPanes[i].Visible = i == index;
        }

        //Save only means something on the Lines tab; the bar re-packs so Close doesn't sit beside a gap.
        SaveButton.Visible = index == 0;
        RelayoutBottomBar();

        if (index == 0)
            FocusLine(0);
        else
            ClearFocus();
    }

    // ── show / hide ──

    /// <summary>
    ///     Opens the popup for a castable slot, on the Lines tab. <paramref name="chants" /> may be shorter than
    ///     <paramref name="lineCount" />; missing lines start empty.
    /// </summary>
    public void Show(
        byte slot,
        string name,
        string level,
        Texture2D? icon,
        string[] chants,
        int lineCount,
        bool isSpell)
    {
        EditingSlot = slot;
        IsSpell = isSpell;
        LineCount = Math.Clamp(lineCount, 0, MAX_LINES);

        SetTitle(name);

        for (var i = 0; i < MAX_LINES; i++)
        {
            TextInputs[i].Text = i < Math.Min(LineCount, chants.Length) ? chants[i] : string.Empty;
            TextInputs[i].Visible = i < LineCount;
        }

        Details.Bind(slot, name, level, icon, isSpell);
        SelectTab(0);
        base.Show();
    }

    public override void Hide()
    {
        if (!Visible)
            return;

        ClearFocus();
        Details.Release();
        base.Hide();
    }

    private void Confirm()
    {
        var chants = new string[LineCount];

        for (var i = 0; i < LineCount; i++)
            chants[i] = TextInputs[i].Text;

        OnChantSet?.Invoke(EditingSlot, chants, IsSpell);
        Hide();
    }

    // ── focus ──

    private void FocusLine(int index)
    {
        for (var i = 0; i < MAX_LINES; i++)
            TextInputs[i].IsFocused = (i == index) && (i < LineCount);
    }

    private void ClearFocus()
    {
        foreach (var box in TextInputs)
            box.IsFocused = false;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        //Enter commits from the Lines tab only — on Details there is nothing to commit, so the base's
        //swallow-everything behavior applies instead.
        if ((e.Key == Keys.Enter) && TabPanes[0].Visible)
        {
            Confirm();
            e.Handled = true;

            return;
        }

        if ((e.Key == Keys.Tab) && TabPanes[0].Visible && (LineCount > 1))
        {
            for (var i = 0; i < LineCount; i++)
                if (TextInputs[i].IsFocused)
                {
                    FocusLine((i + 1) % LineCount);

                    break;
                }

            e.Handled = true;

            return;
        }

        base.OnKeyDown(e);
    }

    /// <summary>
    ///     The Details tab body: icon + level header, the metadata requirement rows (level/ability/master, the five
    ///     stats, up to two prerequisites), a live slot-state line, and the scrollable description. Every requirement
    ///     is coloured through <see cref="AbilityRequirements" />, so it reads identically to the status-book popup.
    /// </summary>
    private sealed class DetailsPane : UIPanel
    {
        private const int ICON_SIZE = 32;
        private const int ROW_H = 14;
        private const int ROW_GAP = 2;
        private const int ICON_TO_TEXT = 8;
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
        private readonly ScrollView DescriptionScroll;
        private readonly UILabel Description;

        public DetailsPane(Rectangle pane)
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

            //one label per stat so each carries its own met/unmet colour.
            var statY = ICON_SIZE + ROW_GAP * 2;
            var statW = pane.Width / STAT_COUNT;

            for (var i = 0; i < STAT_COUNT; i++)
                Stats[i] = AddRow(i * statW, statY, statW);

            PreReq1 = AddRow(0, statY + ROW_H + ROW_GAP, pane.Width);
            PreReq2 = AddRow(0, statY + 2 * ROW_H + ROW_GAP, pane.Width);
            SpellState = AddRow(0, statY + 3 * ROW_H + ROW_GAP, pane.Width);

            var descTop = statY + 4 * ROW_H + ROW_GAP * 3;

            DescriptionScroll = new ScrollView
            {
                X = 0,
                Y = descTop,
                Width = pane.Width,
                Height = pane.Height - descTop
            };

            Description = new UILabel
            {
                X = 0,
                Y = 0,
                Width = DescriptionScroll.ContentWidth,
                Height = DescriptionScroll.ContentHeight,
                PaddingLeft = 0,
                PaddingRight = 2,
                PaddingTop = 0,
                WordWrap = true,
                ForegroundColor = TextColors.Default,
                IsSelectable = true
            };

            DescriptionScroll.AddChild(Description);
            DescriptionScroll.SetSource(new LabelScrollSource(Description));
            AddChild(DescriptionScroll);
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
            var hasMetadata = WorldState.TryGetAbilityMetadata(name, isSpell, out var entry) && (entry is not null);

            //the metadata icon carries the learnable/locked duotone treatment; without metadata, fall back to the
            //slot's own texture (always present — right-click is gated on it).
            if (hasMetadata)
            {
                var resolved = AbilityRequirements.ResolveIcon(entry!);
                Icon.Texture = resolved.Texture;
                Icon.TextureOffset = new Vector2(resolved.OffsetX, resolved.OffsetY);
            } else
            {
                Icon.Texture = icon;
                Icon.TextureOffset = Vector2.Zero;
            }

            Header.Text = string.IsNullOrEmpty(level) ? name : $"{name}  (Lev {level})";
            Live.Text = FormatLiveState(slot, isSpell);
            SpellState.Text = isSpell ? FormatSpellState(slot) : string.Empty;

            if (!hasMetadata)
            {
                Requirement.Text = string.Empty;
                PreReq1.Text = string.Empty;
                PreReq2.Text = string.Empty;

                foreach (var label in Stats)
                    label.Text = string.Empty;

                Description.Text = "No metadata available for this ability.";
                Description.ForegroundColor = DialogPalette.DisabledText;
                DescriptionScroll.Sync();

                return;
            }

            var attrs = WorldState.Attributes.Current;

            if (entry!.RequiresMaster)
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

            Description.Text = entry.Description;
            Description.ForegroundColor = TextColors.Default;
            DescriptionScroll.Sync();
        }

        /// <summary>Drops the icon reference when the popup closes, mirroring the legacy popup's Hide.</summary>
        public void Release() => Icon.Texture = null;

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

        private static string FormatLiveState(byte slot, bool isSpell)
        {
            var remainingMs = isSpell
                ? WorldState.SpellBook.GetCooldownRemainingMs(slot)
                : WorldState.SkillBook.GetCooldownRemainingMs(slot);

            return $"Slot {slot} — {(remainingMs > 0 ? $"cooldown {remainingMs / 1000f:0.0}s" : "ready")}";
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
}
