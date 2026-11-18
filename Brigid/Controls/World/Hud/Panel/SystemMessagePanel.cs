#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Rendering;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud.Panel;

/// <summary>
///     Message history panel (Shift+F). Displays server message history (same text as the orange bar) in its own tab-sized
///     panel. Reads from the shared history list and preserves the per-message color (orange for system messages,
///     whisper/group/guild colors for echoed chat). A message that is wider than a line wraps onto the lines below it, so
///     the history shows each message whole; the orange bar shows only the part that fits.
/// </summary>
public sealed class SystemMessagePanel : ExpandablePanel
{
    private const int GLYPH_HEIGHT = 12;
    private readonly ScrollBarControl Bar;
    private readonly ScrollBarBinder Binder;
    private readonly IReadOnlyList<Chat.OrangeBarMessage> History;
    //the wrapped lines of History; the scroll model counts these lines, not the messages
    private List<Chat.OrangeBarMessage> DisplayLines = [];
    private readonly FontGenerationStamp LayoutStamp = new();
    private readonly int LineWidth;
    //Offset is lines-from-bottom (0 = newest at bottom); Inverted maps wheel/page + the bar thumb accordingly
    private readonly ScrollModel Model = new() { Inverted = true };
    private readonly Rectangle NormalDisplayBounds;
    private readonly int PanelOriginX;
    private readonly int PanelOriginY;

    private Rectangle DisplayBounds;
    private Rectangle ExpandedDisplayBounds;
    //set by the Chat events, not by a change in History.Count: the history is a ring buffer, and its count stops
    //changing when the buffer is full
    private bool HistoryChanged = true;
    private UILabel[] Lines;
    private int MaxVisibleLines;
    private int RenderedScrollOffset = -1;

    public SystemMessagePanel(Rectangle displayBounds, Rectangle panelBounds)
    {
        Name = "MessageHistory";
        NormalDisplayBounds = displayBounds;
        DisplayBounds = displayBounds;
        PanelOriginX = panelBounds.X;
        PanelOriginY = panelBounds.Y;
        History = WorldState.Chat.GetOrangeBarHistory();

        Background = UiRenderer.Instance!.GetSpfTexture("_nchatbk.spf");

        MaxVisibleLines = displayBounds.Height > 0 ? displayBounds.Height / GLYPH_HEIGHT : 0;
        Lines = new UILabel[MaxVisibleLines];

        var relX = displayBounds.X - panelBounds.X;
        LineWidth = displayBounds.Width - ScrollBarControl.DEFAULT_WIDTH;

        for (var i = 0; i < MaxVisibleLines; i++)
        {
            Lines[i] = new UILabel
            {
                Name = $"HistoryLine{i}",
                X = relX,
                Width = LineWidth,
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

        WorldState.Chat.OrangeBarMessageAdded += OnOrangeBarMessageAdded;
        WorldState.Chat.Cleared += OnChatCleared;
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
                    Width = LineWidth,
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

    public override void Dispose()
    {
        WorldState.Chat.OrangeBarMessageAdded -= OnOrangeBarMessageAdded;
        WorldState.Chat.Cleared -= OnChatCleared;

        base.Dispose();
    }

    private void OnChatCleared() => HistoryChanged = true;

    private void OnOrangeBarMessageAdded(Chat.OrangeBarMessage _) => HistoryChanged = true;

    /// <summary>
    ///     The lines of <paramref name="history" /> wrapped at <paramref name="width" />, oldest first. Each line keeps the
    ///     color of its message, and an empty message is one empty line.
    /// </summary>
    internal static List<Chat.OrangeBarMessage> WrapHistory(IReadOnlyList<Chat.OrangeBarMessage> history, int width)
    {
        var lines = new List<Chat.OrangeBarMessage>();

        for (var i = 0; i < history.Count; i++)
        {
            var message = history[i];
            var wrapped = TextRenderer.WrapLines(message.Text, width);

            if (wrapped.Count == 0)
                lines.Add(message with { Text = string.Empty });
            else
                lines.AddRange(wrapped.Select(line => message with { Text = line }));
        }

        return lines;
    }

    private void RefreshDisplay()
    {
        if (Model.Offset == RenderedScrollOffset)
            return;

        RenderedScrollOffset = Model.Offset;

        var maxLines = Math.Min(MaxVisibleLines, Lines.Length);
        var startIndex = Math.Max(0, DisplayLines.Count - maxLines - Model.Offset);
        var lineIndex = 0;

        for (var i = startIndex; (i < DisplayLines.Count) && (lineIndex < maxLines); i++)
        {
            var msg = DisplayLines[i];
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

        Model.SetMetrics(DisplayLines.Count, MaxVisibleLines);

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

        //a new face or window size changes the wrap, as a new message does
        if (HistoryChanged || LayoutStamp.Changed)
        {
            var wasAtBottom = Model.Offset == 0;
            HistoryChanged = false;
            RenderedScrollOffset = -1;
            DisplayLines = WrapHistory(History, LineWidth);
            LayoutStamp.Mark();

            Model.SetMetrics(DisplayLines.Count, MaxVisibleLines);

            //stick to the bottom when already there; otherwise keep the same lines-from-bottom offset. The offset counts
            //from the newest line, so an entry evicted from the full buffer needs no change to it
            if (wasAtBottom)
                Model.ScrollToStart();
        }

        RefreshDisplay();
    }
}