#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     Message history panel (Shift+F). Displays server message history (same text as the orange bar) in its own tab-sized
///     panel. Reads from the shared history list and preserves the per-message color (orange for system messages,
///     whisper/group/guild colors for echoed chat).
/// </summary>
public sealed class SystemMessagePanel : ExpandablePanel
{
    private const int GLYPH_HEIGHT = 12;
    private readonly ScrollBarControl Bar;
    private readonly ScrollBarBinder Binder;
    private readonly IReadOnlyList<Chat.OrangeBarMessage> History;
    //Offset is lines-from-bottom (0 = newest at bottom); Inverted maps wheel/page + the bar thumb accordingly
    private readonly ScrollModel Model = new() { Inverted = true };
    private readonly Rectangle NormalDisplayBounds;
    private readonly int PanelOriginX;
    private readonly int PanelOriginY;

    private Rectangle DisplayBounds;
    private Rectangle ExpandedDisplayBounds;
    private int LastHistoryCount;
    private UILabel[] Lines;
    private int MaxVisibleLines;
    private int RenderedHistoryCount = -1;
    private int RenderedScrollOffset = -1;

    public SystemMessagePanel(Rectangle displayBounds, Rectangle panelBounds, IReadOnlyList<Chat.OrangeBarMessage> history)
    {
        Name = "MessageHistory";
        NormalDisplayBounds = displayBounds;
        DisplayBounds = displayBounds;
        PanelOriginX = panelBounds.X;
        PanelOriginY = panelBounds.Y;
        History = history;

        Background = UiRenderer.Instance!.GetSpfTexture("_nchatbk.spf");

        MaxVisibleLines = displayBounds.Height > 0 ? displayBounds.Height / GLYPH_HEIGHT : 0;
        Lines = new UILabel[MaxVisibleLines];

        var relX = displayBounds.X - panelBounds.X;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            Lines[i] = new UILabel
            {
                Name = $"HistoryLine{i}",
                X = relX,
                Width = displayBounds.Width - ScrollBarControl.DEFAULT_WIDTH,
                Height = GLYPH_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(Lines[i]);
        }

        RepositionLabels();

        var relY = displayBounds.Y - panelBounds.Y;

        Bar = new ScrollBarControl
        {
            X = relX + displayBounds.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = relY,
            Height = displayBounds.Height
        };

        Binder = new ScrollBarBinder(Model, Bar);
        Model.Changed += _ => RenderedScrollOffset = -1;

        AddChild(Bar);
    }

    /// <summary>
    ///     Configures expand support for the large HUD message history panel (larger text area).
    /// </summary>
    public void ConfigureExpand(Texture2D? expandedBackground, Rectangle expandedBounds, Rectangle panelBounds)
    {
        ExpandedDisplayBounds = expandedBounds;

        //clear the normal background so expandyoffset is computed from panel height, not the
        //texture height (which is the same as the expanded texture, yielding expandyoffset=0).
        Background = null;
        Height = panelBounds.Height;

        ConfigureExpand(expandedBackground);

        //create additional labels needed for the expanded line count
        var expandedMaxLines = expandedBounds.Height / GLYPH_HEIGHT;

        if (expandedMaxLines > Lines.Length)
        {
            var relX = NormalDisplayBounds.X - PanelOriginX;
            var relY = NormalDisplayBounds.Y - PanelOriginY;
            var oldCount = Lines.Length;
            Array.Resize(ref Lines, expandedMaxLines);

            for (var i = oldCount; i < expandedMaxLines; i++)
            {
                Lines[i] = new UILabel
                {
                    Name = $"HistoryLine{i}",
                    X = relX,
                    Y = relY + NormalDisplayBounds.Height - (MaxVisibleLines - i) * GLYPH_HEIGHT,
                    Width = NormalDisplayBounds.Width - ScrollBarControl.DEFAULT_WIDTH,
                    Height = GLYPH_HEIGHT,
                    PaddingLeft = 0,
                    PaddingTop = 0,
                    Visible = false
                };

                AddChild(Lines[i]);
            }
        }

        //in the large hud, the compact area is too small for a scrollbar
        Bar.Visible = false;
    }

    //labels are children — drawn automatically by base.draw()

    private void RefreshDisplay()
    {
        if ((History.Count == RenderedHistoryCount) && (Model.Offset == RenderedScrollOffset))
            return;

        RenderedHistoryCount = History.Count;
        RenderedScrollOffset = Model.Offset;

        var maxLines = Math.Min(MaxVisibleLines, Lines.Length);
        var startIndex = Math.Max(0, History.Count - maxLines - Model.Offset);
        var lineIndex = 0;

        for (var i = startIndex; (i < History.Count) && (lineIndex < maxLines); i++)
        {
            var msg = History[i];
            Lines[lineIndex].Text = msg.Text;
            Lines[lineIndex].ForegroundColor = msg.Color;
            lineIndex++;
        }

        for (; lineIndex < maxLines; lineIndex++)
            Lines[lineIndex].Text = string.Empty;
    }

    private void RepositionLabels()
    {
        var relY = DisplayBounds.Y - PanelOriginY;
        var maxLines = Math.Min(MaxVisibleLines, Lines.Length);

        for (var i = 0; i < Lines.Length; i++)
            if (i < maxLines)
            {
                Lines[i].Y = relY + DisplayBounds.Height - (maxLines - i) * GLYPH_HEIGHT;
                Lines[i].Visible = true;
            } else
                Lines[i].Visible = false;
    }

    public override void SetExpanded(bool expanded)
    {
        base.SetExpanded(expanded);

        DisplayBounds = expanded ? ExpandedDisplayBounds : NormalDisplayBounds;
        MaxVisibleLines = Math.Min(DisplayBounds.Height / GLYPH_HEIGHT, Lines.Length);
        Bar.Visible = expanded;
        Bar.Height = DisplayBounds.Height;

        Model.SetMetrics(History.Count, MaxVisibleLines);

        //show/hide labels based on current line count
        for (var i = 0; i < Lines.Length; i++)
            Lines[i].Visible = i < MaxVisibleLines;

        RenderedScrollOffset = -1;
    }

    public void ScrollToBottom()
    {
        Model.ScrollToStart();
        RenderedScrollOffset = -1;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (Scroll(e.Delta))
            e.Handled = true;
    }

    public bool Scroll(int delta)
    {
        if (!Model.CanScroll)
            return false;

        //Inverted: a positive wheel notch scrolls back into history; consume while scrollable (matches legacy)
        Model.WheelBy(delta);

        return true;
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        base.Update(gameTime);

        if (History.Count != LastHistoryCount)
        {
            var wasAtBottom = Model.Offset == 0;
            LastHistoryCount = History.Count;

            Model.SetMetrics(History.Count, MaxVisibleLines);

            //stick to the bottom when already there; otherwise keep the same lines-from-bottom offset
            if (wasAtBottom)
                Model.ScrollToStart();
        }

        RefreshDisplay();
    }
}