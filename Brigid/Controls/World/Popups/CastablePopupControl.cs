#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
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
///     Each tab sets its own panel height, because their vertical needs genuinely differ: Lines shows six chant
///     rows and scrolls past that (nine is the ceiling — Dark Ages' cap, and Hybrasyl uses at most four), while
///     Details needs room for a description Hybrasyl pads out to ten lines. That is still not the legacy popup's
///     per-line-count juggling: the height depends on the view, not on how many lines the castable happens to
///     have, so opening the same tab always looks the same.
/// </summary>
public sealed class CastablePopupControl : CenteredModalPanel
{
    private const int PANEL_W = 320;

    //one height per tab: enough for VISIBLE_LINES chant rows, and enough for a readable description.
    private const int LINES_PANEL_H = 209;
    private const int DETAILS_PANEL_H = 264;

    private const int TAB_W = 72;
    private const int TAB_H = 20;
    private const int TAB_GAP = 4;
    private const int TAB_TO_PANE = 6;

    //Dark Ages tops out at nine chant lines and Hybrasyl at four. The wire field (0x17 AddSpell "lines") is a
    //byte, so Show clamps rather than trusting it — a server sending more just loses the extra rows.
    private const int MAX_LINES = 9;

    //rows on screen at once; a longer chant scrolls rather than making the popup taller for every castable.
    private const int VISIBLE_LINES = 6;
    private const int LINE_H = 20;
    private const int LINE_STRIDE = 22;
    private const int CHANT_MAX_LENGTH = 32;
    private const int LINE_TO_BAR = 2;
    private const int SAVE_W = 52;

    private enum CastableTab
    {
        Lines,
        Details
    }

    private const int TAB_COUNT = 2;

    private static readonly string[] TabTitles = ["Lines", "Details"];

    private readonly TabStrip Tabs;
    private readonly UIPanel[] TabPanes = new UIPanel[TAB_COUNT];

    private readonly UITextBox[] TextInputs = new UITextBox[MAX_LINES];
    private ScrollBarControl LineScrollBar = null!;
    private ScrollModel LineScroll = null!;
    private ScrollBarBinder LineBarBinder = null!;
    private readonly TextButton SaveButton;
    private readonly CastableDetailsPane Details;

    private CastableTab Active = CastableTab.Lines;

    private byte EditingSlot;
    private bool IsSpell;
    private int LineCount;

    /// <summary>Raised when the chant lines are committed. Parameters: slot (1-based), lines, isSpell.</summary>
    public event ChantSetHandler? OnChantSet;

    public CastablePopupControl()
        : base("Castable", PANEL_W, DETAILS_PANEL_H)
    {
        Name = "CastablePopup";

        var content = ContentBounds;

        //each pane is built at its own tab's height; SelectTab re-heights the panel to match.
        var paneTop = content.Y + TAB_H + TAB_TO_PANE;
        var paneWidth = content.Width;
        var linesPane = new Rectangle(content.X, paneTop, paneWidth, LINES_PANEL_H - BOTTOM_H - paneTop);
        var detailsPane = new Rectangle(content.X, paneTop, paneWidth, DETAILS_PANEL_H - BOTTOM_H - paneTop);

        Tabs = BuildTabRow(content);
        SaveButton = AddBottomBarButton("Save", SAVE_W, Confirm);

        BuildLinesPane(linesPane);
        Details = BuildDetailsPane(detailsPane);

        SelectTab(CastableTab.Lines);
    }

    private TabStrip BuildTabRow(Rectangle content)
    {
        var strip = new TabStrip(TabTitles, TAB_W, TAB_H, TAB_GAP)
        {
            X = content.X,
            Y = content.Y
        };

        strip.TabClicked += index => SelectTab((CastableTab)index);
        AddChild(strip);

        return strip;
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

        //the bar sits beside the boxes and is driven by the shared scroll model; it hides when every line fits.
        LineScrollBar = new ScrollBarControl
        {
            Name = "ChantScrollBar",
            X = pane.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = 0,
            Height = VISIBLE_LINES * LINE_STRIDE
        };

        var boxWidth = pane.Width - ScrollBarControl.DEFAULT_WIDTH - LINE_TO_BAR;

        for (var i = 0; i < MAX_LINES; i++)
        {
            TextInputs[i] = new UITextBox
            {
                Name = $"ChantLine{i}",
                X = 0,
                Y = i * LINE_STRIDE,
                Width = boxWidth,
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

        content.AddChild(LineScrollBar);

        LineScroll = new ScrollModel();
        LineBarBinder = new ScrollBarBinder(LineScroll, LineScrollBar);
        LineScroll.Changed += _ => LayoutLines();

        TabPanes[(int)CastableTab.Lines] = content;
        AddChild(content);
    }

    /// <summary>
    ///     Positions the chant boxes for the current scroll offset, showing the <see cref="VISIBLE_LINES" /> rows in
    ///     the window and hiding the rest. Hidden boxes drop out of the tab-stop walk and the slot chrome, so a
    ///     scrolled-away line can neither be focused nor painted.
    /// </summary>
    private void LayoutLines()
    {
        var offset = LineScroll.Offset;

        for (var i = 0; i < MAX_LINES; i++)
        {
            var row = i - offset;
            var onScreen = (i < LineCount) && (row >= 0) && (row < VISIBLE_LINES);

            TextInputs[i].Visible = onScreen;

            if (onScreen)
                TextInputs[i].Y = row * LINE_STRIDE;
        }
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
        Tabs.SetSelected((int)tab);
        SetPanelHeight(tab == CastableTab.Lines ? LINES_PANEL_H : DETAILS_PANEL_H);

        for (var i = 0; i < TAB_COUNT; i++)
            TabPanes[i].Visible = i == (int)tab;

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
            TextInputs[i].Text = i < Math.Min(LineCount, chants.Length) ? chants[i] : string.Empty;

        LineScroll.SetMetrics(LineCount, VISIBLE_LINES);
        LineScroll.ScrollToStart();
        LineScrollBar.Visible = LineCount > VISIBLE_LINES;
        LayoutLines();

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

    /// <summary>
    ///     Routes the wheel to the chant lines. The base absorbs scroll so it can't reach the world, so the Lines
    ///     tab has to claim it here.
    /// </summary>
    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if ((Active == CastableTab.Lines) && (LineCount > VISIBLE_LINES))
            LineScroll.WheelBy(e.Delta);

        e.Handled = true;
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

        //Details: the description gets its clipboard/caret keys first, then the base swallows whatever is left.
        //Escape is the base's (it closes), so it must not reach a pane that is about to be hidden.
        if (e.Key != Keys.Escape)
            Details.ForwardKey(e);

        base.OnKeyDown(e);
    }
}
