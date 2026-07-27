#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     Chat display panel (F key). Shows chat message history with word-wrap. Background loaded from _nchatbk.spf (shown
///     in tab area). Text rendered at ChatDisplayBounds (separate area of the HUD).
/// </summary>
public sealed class ChatPanel : ExpandablePanel
{
    private const int MAX_CHAT_LINES = 200;
    private const int GLYPH_HEIGHT = 12;

    //widest line the panel is expected to show without wrapping. The server sends chat pre-prefixed as
    //"{name}: {message}" (whispers use {name}" / {name}>), names are 4-12 characters (server-side validation) and the
    //retail say/shout/whisper input caps the message at 55 — so 12 + 2 + 55 is the worst case, not 55.
    private const int AUTOFIT_BUDGET_CHARS = 12 + 2 + 55;

    //cancels the global negative tracking for chat. None of the shipped faces carry sidebearing slack — glyph ink
    //fills its advance cell exactly (Anonymous Pro's actually overflows it) — so the default -1px pulls each glyph
    //into its neighbour. Sizing and drawing at the natural advance keeps letters apart; the size is picked to fill
    //the line at that spacing, so the width is still used, just by a larger glyph rather than a squeezed one.
    private const float NATURAL_SPACING = -FontEngine.DEFAULT_TRACKING;

    //raw messages as received; wrapping is derived (see RebuildRenderLines), never stored, so a font-size or width
    //change re-wraps the whole backlog instead of leaving old lines broken at the previous width.
    private readonly List<ChatLine> Messages = [];
    private readonly List<ChatLine> RenderLines = [];

    //RenderLines produced by Messages[i], so evicting the oldest message drops exactly its lines
    private readonly List<int> MessageLineCounts = [];
    private readonly ScrollBarControl Bar;
    private readonly ScrollBarBinder Binder;
    //Offset is lines-from-bottom (0 = newest at bottom); Inverted maps wheel/page + the bar thumb accordingly
    private readonly ScrollModel Model = new() { Inverted = true };
    private readonly Rectangle NormalDisplayBounds;
    private readonly int PanelOriginX;
    private readonly int PanelOriginY;

    private Rectangle DisplayBounds;
    private Rectangle ExpandedDisplayBounds;
    private UILabel[] Lines;
    private int LogVersion;
    private int FontSize = FontEngine.RENDER_SIZE;
    private int LayoutFontGeneration = -1;
    private int LayoutWidth = -1;
    private int MaxVisibleLines;
    private int RenderedVersion = -1;

    public ChatPanel(Rectangle displayBounds, Rectangle panelBounds)
    {
        Name = "Chat";
        NormalDisplayBounds = displayBounds;
        DisplayBounds = displayBounds;
        PanelOriginX = panelBounds.X;
        PanelOriginY = panelBounds.Y;

        Background = UiRenderer.Instance!.GetSpfTexture("_nchatbk.spf");

        MaxVisibleLines = displayBounds.Height > 0 ? displayBounds.Height / GLYPH_HEIGHT : 0;
        Lines = new UILabel[MaxVisibleLines];

        var relX = displayBounds.X - panelBounds.X;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            Lines[i] = new UILabel
            {
                Name = $"ChatLine{i}",
                X = relX,
                Width = displayBounds.Width - ScrollBarControl.DEFAULT_WIDTH,
                Height = GLYPH_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(Lines[i]);
        }

        RepositionLabels();

        //position relative to panel origin (panel is placed at panelbounds by registertab)
        var relY = displayBounds.Y - panelBounds.Y;

        Bar = new ScrollBarControl
        {
            X = relX + displayBounds.Width - ScrollBarControl.DEFAULT_WIDTH,
            Y = relY,
            Height = displayBounds.Height
        };

        Binder = new ScrollBarBinder(Model, Bar);
        Model.Changed += _ => LogVersion++;

        AddChild(Bar);
        WorldState.Chat.MessageAdded += OnMessageAdded;
    }

    private void AddMessage(string text, Color color)
    {
        var message = new ChatLine(text, color);
        Messages.Add(message);

        //wrap only the arrival, and drop the evicted message's lines by its recorded count — re-wrapping the whole
        //backlog per message would be quadratic over a session, and chat is the one panel that updates constantly
        MessageLineCounts.Add(WrapInto(message, DisplayBounds.Width - ScrollBarControl.DEFAULT_WIDTH, RenderLines));

        while (Messages.Count > MAX_CHAT_LINES)
        {
            RenderLines.RemoveRange(0, MessageLineCounts[0]);
            MessageLineCounts.RemoveAt(0);
            Messages.RemoveAt(0);
        }

        var wasAtBottom = Model.Offset == 0;

        Model.SetMetrics(RenderLines.Count, MaxVisibleLines);

        //stick to the bottom when already there; otherwise SetMetrics keeps the same lines-from-bottom offset
        if (wasAtBottom)
            Model.ScrollToStart();

        LogVersion++;
    }

    /// <summary>
    ///     Configures expand support for the large HUD chat panel (larger text area).
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
                    Name = $"ChatLine{i}",
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

        //in the large hud, the compact chat area is too small for a scrollbar
        Bar.Visible = false;
    }

    public override void Dispose()
    {
        WorldState.Chat.MessageAdded -= OnMessageAdded;

        base.Dispose();
    }

    //labels are children — drawn automatically by base.draw()

    private void OnMessageAdded(Chat.ChatMessage msg) => AddMessage(msg.Text, msg.Color);

    /// <summary>
    ///     Recomputes the autofit glyph size and re-wraps every held message at it. Wrapping is derived state: the size
    ///     depends on the active face (faces differ by up to ~40% in advance width at the same pixel size, so no fixed
    ///     size holds the no-wrap goal across a font switch) and on the panel width, so both must be able to change
    ///     without leaving stale line breaks behind.
    ///     <para>
    ///         Wrapping is kept as a backstop rather than removed: the autofit budget covers the ASCII the DA protocol
    ///         carries, but fallback faces (CJK, emoji) do not share the monospace advance, so an unusual message can
    ///         still overflow and must break rather than run past the panel.
    ///     </para>
    /// </summary>
    private void RebuildRenderLines()
    {
        var maxWidth = DisplayBounds.Width - ScrollBarControl.DEFAULT_WIDTH;

        RenderLines.Clear();
        MessageLineCounts.Clear();

        if (maxWidth <= 0)
        {
            //keep one count per message even with no usable width, or the two lists desync and an eviction
            //later removes the wrong lines
            foreach (var _ in Messages)
                MessageLineCounts.Add(0);

            return;
        }

        FontSize = FontEngine.Instance.LargestSizeFitting(
            AUTOFIT_BUDGET_CHARS,
            maxWidth,
            extraSpacing: NATURAL_SPACING);
        LayoutWidth = maxWidth;
        LayoutFontGeneration = FontEngine.Instance.Generation;

        foreach (var message in Messages)
            MessageLineCounts.Add(WrapInto(message, maxWidth, RenderLines));
    }

    //appends message's wrapped lines to sink and returns how many were added, so an eviction can drop exactly that
    //many. An empty message yields zero lines, matching the pre-existing behaviour of dropping blanks entirely.
    private int WrapInto(ChatLine message, int maxWidth, List<ChatLine> sink)
    {
        if (maxWidth <= 0)
            return 0;

        var remaining = message.Text;
        var added = 0;

        while (remaining.Length > 0)
        {
            var lineEnd = TextRenderer.FindLineBreak(remaining, maxWidth, size: FontSize, extraSpacing: NATURAL_SPACING);

            sink.Add(
                new ChatLine(
                    remaining[..lineEnd]
                        .TrimEnd(),
                    message.Color));

            remaining = remaining[lineEnd..]
                .TrimStart();
            added++;
        }

        return added;
    }

    //re-wrap when the active face changed (the user cycled fonts) or the panel width moved under us; both invalidate
    //the autofit size and every stored line break. Cheap no-op otherwise — this runs every frame.
    private void EnsureLayoutCurrent()
    {
        var maxWidth = DisplayBounds.Width - ScrollBarControl.DEFAULT_WIDTH;

        if ((maxWidth == LayoutWidth) && (FontEngine.Instance.Generation == LayoutFontGeneration))
            return;

        RebuildRenderLines();
        Model.SetMetrics(RenderLines.Count, MaxVisibleLines);
        LogVersion++;
    }

    private void RefreshDisplay()
    {
        if (RenderedVersion == LogVersion)
            return;

        RenderedVersion = LogVersion;

        var maxLines = Math.Min(MaxVisibleLines, Lines.Length);
        var startIndex = Math.Max(0, RenderLines.Count - maxLines - Model.Offset);
        var lineIndex = 0;

        for (var i = startIndex; (i < RenderLines.Count) && (lineIndex < maxLines); i++)
        {
            var line = RenderLines[i];
            Lines[lineIndex].Text = line.Text;
            Lines[lineIndex].ForegroundColor = line.Color;
            Lines[lineIndex].FontSize = FontSize;
            Lines[lineIndex].CharacterSpacing = NATURAL_SPACING;
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
                //bottom-up: line 0 at top, line maxlines-1 at bottom
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

        RebuildRenderLines();
        Model.SetMetrics(RenderLines.Count, MaxVisibleLines);

        //show/hide labels based on current line count
        for (var i = 0; i < Lines.Length; i++)
            Lines[i].Visible = i < MaxVisibleLines;

        //force re-render with new line count
        LogVersion++;
    }

    public void ScrollToBottom()
    {
        Model.ScrollToStart();
        LogVersion++;
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

        EnsureLayoutCurrent();
        RefreshDisplay();
    }

    private record struct ChatLine(string Text, Color Color);
}