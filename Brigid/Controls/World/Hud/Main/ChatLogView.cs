#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.ViewModel;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The scrollback of the chat box: the lines of a <see cref="ChatLogWindow" /> in labels, the newest at the bottom,
///     with the chat box's own scroll bar at the right. The text uses the normal UI size and tracking, and a long line
///     wraps (BRIG-127 D22). Each entry keeps its wrapped lines until the width or <see cref="FontEngine.Generation" />
///     changes; then only the entries that the window reads wrap again, so a window drag does not wrap the backlog
///     (BRIG-1).
///     <para>
///         The chat box sets the height of the line area (<see cref="SetLineArea" />): the lines go down to the bottom of
///         the box, and move up while the chat input shows under them. The scroll bar counts entries, and its page is the
///         number of entries that the lines show.
///     </para>
/// </summary>
internal sealed class ChatLogView : UIPanel, IChatTextStyle
{
    private const int LINE_HEIGHT = TextRenderer.CHAR_HEIGHT;

    //the normal tracking of UI text; the one spacing that the lines, the wrap and the chat input use
    private const float SPACING = 0f;

    //the most wrapped entries that the view keeps; the log holds 1000
    private const int MAX_CACHED = 1200;

    private readonly ChatScrollBar Bar;
    private readonly Dictionary<long, IReadOnlyList<string>> Wrapped = [];
    private readonly UILabel[] Lines;
    private readonly Chat Log;
    private readonly ScrollModel Model = new() { Inverted = true };
    private readonly FontGenerationStamp Stamp = new();
    private bool Dirty = true;
    private bool Syncing;
    private int WrappedWidth = -1;

    /// <param name="log">The chat log.</param>
    /// <param name="bounds">The view at its tallest, in the coordinates of the chat box.</param>
    public ChatLogView(Chat log, Rectangle bounds)
    {
        Name = "chat_log";
        Log = log;
        SetBounds(bounds);

        Lines = new UILabel[bounds.Height / LINE_HEIGHT];
        Window = new ChatLogWindow(log, Lines.Length, WrapEntry);

        for (var i = 0; i < Lines.Length; i++)
        {
            Lines[i] = new UILabel
            {
                Name = $"line_{i}",
                X = 0,
                Width = TextWidth,
                Height = LINE_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0,
                ShrinkToFit = false,
                IsHitTestVisible = false
            };

            AddChild(Lines[i]);
        }

        Bar = new ChatScrollBar(Model, new Rectangle(bounds.Width - MainHudLayout.CHAT_SCROLL_WIDTH, 0, MainHudLayout.CHAT_SCROLL_WIDTH, bounds.Height));
        AddChild(Bar);
        Model.Changed += OnModelChanged;

        Log.EntryAdded += OnEntryAdded;
        Log.Cleared += OnCleared;

        SetLineArea(bounds.Height);
    }

    /// <summary>The window over the log that the view shows.</summary>
    internal ChatLogWindow Window { get; }

    /// <summary>The scroll bar of the view.</summary>
    internal ChatScrollBar ScrollBar => Bar;

    /// <summary>The model of the scroll bar: the entries of the tab, and the entries below the bottom one.</summary>
    internal ScrollModel ScrollModel => Model;

    /// <summary>The text of the lines that the view shows, from the top; an empty line has empty text.</summary>
    internal IEnumerable<string> ShownText => Lines.Where(line => line.Visible).Select(line => line.Text);

    /// <summary>The labels of the lines that the view shows, from the top.</summary>
    internal IEnumerable<UILabel> ShownLines => Lines.Where(line => line.Visible);

    private int TextWidth => Width - MainHudLayout.CHAT_SCROLL_WIDTH - 2;

    private static int TextSize => FontEngine.Instance.UiSize;

    /// <summary>
    ///     Makes the line area <paramref name="height" /> tall, from the top of the view: the lines that fit show, and
    ///     the last line touches the bottom of the area.
    /// </summary>
    public void SetLineArea(int height)
    {
        var count = Math.Clamp(height / LINE_HEIGHT, 1, Lines.Length);
        var top = height - count * LINE_HEIGHT;

        for (var i = 0; i < Lines.Length; i++)
        {
            Lines[i].Visible = i < count;
            Lines[i].Y = top + i * LINE_HEIGHT;
        }

        Bar.SetBounds(new Rectangle(Bar.X, 0, Bar.Width, height));
        Window.LineCount = count;
        Dirty = true;
    }

    /// <summary>Shows the entries of <paramref name="tab" />, from the newest entry.</summary>
    public void ShowTab(ChatTab tab)
    {
        Window.Tab = tab;
        Dirty = true;
    }

    /// <summary>Scrolls by a wheel delta: a positive delta goes to older lines. Returns false when the view did not move.</summary>
    public bool Scroll(int delta)
    {
        var moved = delta > 0 ? Window.ScrollBack(delta) : Window.ScrollForward(-delta);

        if (moved)
            Dirty = true;

        return moved;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (Scroll(e.Delta))
            e.Handled = true;
    }

    public (int Size, float Spacing) EnsureTextStyle() => (TextSize, SPACING);

    public override void Update(GameTime gameTime)
    {
        if (!Visible)
            return;

        base.Update(gameTime);
        Refresh();
    }

    /// <summary>Brings the labels and the scroll bar up to date: after a change of the log, the position, the width or the face.</summary>
    internal void Refresh()
    {
        if (Stamp.Changed || (WrappedWidth != TextWidth))
        {
            Stamp.Mark();
            WrappedWidth = TextWidth;
            Wrapped.Clear();
            Dirty = true;
        }

        if (!Dirty)
            return;

        Dirty = false;

        var (shown, entries) = Window.Visible();
        var count = Window.LineCount;

        //bottom-aligned: a tab with few lines leaves the top lines empty
        var empty = count - shown.Count;

        for (var i = 0; i < count; i++)
        {
            var line = Lines[i];

            //size and spacing first: assigning Text re-measures
            line.FontSize = TextSize;
            line.CharacterSpacing = SPACING;

            if (i < empty)
                line.Text = string.Empty;
            else
            {
                line.Text = shown[i - empty].Text;
                line.ForegroundColor = shown[i - empty].Color;
            }
        }

        Syncing = true;
        Model.SetMetrics(Window.MatchCount, Math.Max(1, entries));
        Model.ScrollTo(Window.EntriesBelow);
        Syncing = false;
    }

    public override void Dispose()
    {
        Log.EntryAdded -= OnEntryAdded;
        Log.Cleared -= OnCleared;

        base.Dispose();
    }

    //a drag of the scroll bar or a click on its track puts the bottom on an entry
    private void OnModelChanged(int offset)
    {
        if (Syncing)
            return;

        Window.ScrollToEntry(offset);
        Dirty = true;
    }

    //every entry can change the view: one of the tab's entries arrives, or the cap removes one
    private void OnEntryAdded(ChatEntry entry)
    {
        Dirty = true;

        //the cap removes old entries one at a time, so the cache drops the removed ones in batches
        if (Wrapped.Count > MAX_CACHED)
            foreach (var sequence in Wrapped.Keys.Where(sequence => sequence < Log.FirstSequence).ToList())
                Wrapped.Remove(sequence);
    }

    private void OnCleared()
    {
        Wrapped.Clear();
        Window.ScrollToNewest();
        Dirty = true;
    }

    private IReadOnlyList<string> WrapEntry(long sequence, ChatEntry entry)
    {
        if (!Wrapped.TryGetValue(sequence, out var lines))
        {
            lines = TextRenderer.WrapLines(entry.Text, TextWidth, TextSize, SPACING);
            Wrapped[sequence] = lines;
        }

        return lines;
    }
}
