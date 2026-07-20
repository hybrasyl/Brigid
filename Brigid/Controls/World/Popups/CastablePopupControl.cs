#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Systems;
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
///         (<see cref="WorldState.AbilityMetadata" />), colour-coded against the player's live stats, plus live
///         slot state — see <see cref="CastableDetailsPane" />.</item>
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

    private enum CastableTab
    {
        Lines,
        Details
    }

    private const int TAB_COUNT = 2;

    private static readonly string[] TabTitles = ["Lines", "Details"];

    private readonly SelectableTab[] Tabs = new SelectableTab[TAB_COUNT];
    private readonly UIPanel[] TabPanes = new UIPanel[TAB_COUNT];

    private readonly UITextBox[] TextInputs = new UITextBox[MAX_LINES];
    private readonly TextButton SaveButton;
    private readonly CastableDetailsPane Details;

    private CastableTab Active = CastableTab.Lines;

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

        BuildLinesPane(pane);
        Details = BuildDetailsPane(pane);

        SelectTab(CastableTab.Lines);
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

            tab.Clicked += () => SelectTab((CastableTab)index);
            Tabs[i] = tab;
            AddChild(tab);
        }
    }

    private void BuildLinesPane(Rectangle pane)
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
                FocusedBackgroundColor = DialogPalette.RowHoverFill,
                //lets Tab/Shift+Tab traverse the lines through the panel's own tab-stop walk; without it the
                //focused box swallows Tab outright.
                IsTabStop = true
            };

            content.AddChild(TextInputs[i]);
        }

        TabPanes[(int)CastableTab.Lines] = content;
        AddChild(content);
    }

    private CastableDetailsPane BuildDetailsPane(Rectangle pane)
    {
        var content = new CastableDetailsPane(pane);
        TabPanes[(int)CastableTab.Details] = content;
        AddChild(content);

        return content;
    }

    // ── tabs ──

    private void SelectTab(CastableTab tab)
    {
        Active = tab;

        for (var i = 0; i < TAB_COUNT; i++)
        {
            Tabs[i].IsSelected = i == (int)tab;
            TabPanes[i].Visible = i == (int)tab;
        }

        //Save only means something on the Lines tab.
        SetBottomBarActions(tab == CastableTab.Lines ? [SaveButton] : []);

        if (tab == CastableTab.Lines)
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
        SelectTab(CastableTab.Lines);
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
        if (Active == CastableTab.Lines)
        {
            //Enter commits from the Lines tab only — on Details there is nothing to commit.
            if (e.Key == Keys.Enter)
            {
                Confirm();
                e.Handled = true;

                return;
            }

            base.OnKeyDown(e);

            return;
        }

        //Details: give the selectable description its clipboard/caret keys before the base swallows the rest.
        base.OnKeyDown(e);
        Details.ForwardKey(e);
    }
}
