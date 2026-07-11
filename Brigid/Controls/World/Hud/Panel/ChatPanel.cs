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
    private readonly List<ChatLine> ChatLog = [];
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
        var maxWidth = DisplayBounds.Width - ScrollBarControl.DEFAULT_WIDTH;

        if (maxWidth <= 0)
            return;

        var remaining = text;

        while (remaining.Length > 0)
        {
            var lineEnd = TextRenderer.FindLineBreak(remaining, maxWidth);

            var line = remaining[..lineEnd]
                .TrimEnd();

            remaining = remaining[lineEnd..]
                .TrimStart();

            ChatLog.Add(new ChatLine(line, color));
        }

        if (ChatLog.Count > MAX_CHAT_LINES)
            ChatLog.RemoveRange(0, ChatLog.Count - MAX_CHAT_LINES);

        var wasAtBottom = Model.Offset == 0;

        Model.SetMetrics(ChatLog.Count, MaxVisibleLines);

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

    private void RefreshDisplay()
    {
        if (RenderedVersion == LogVersion)
            return;

        RenderedVersion = LogVersion;

        var maxLines = Math.Min(MaxVisibleLines, Lines.Length);
        var startIndex = Math.Max(0, ChatLog.Count - maxLines - Model.Offset);
        var lineIndex = 0;

        for (var i = startIndex; (i < ChatLog.Count) && (lineIndex < maxLines); i++)
        {
            var line = ChatLog[i];
            Lines[lineIndex].Text = line.Text;
            Lines[lineIndex].ForegroundColor = line.Color;
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

        Model.SetMetrics(ChatLog.Count, MaxVisibleLines);

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

        RefreshDisplay();
    }

    private record struct ChatLine(string Text, Color Color);
}