#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive menu that a caller opens at a point (BRIG-125): a frame with a title row and up to
///     <see cref="MaxEntries" /> entries, for the world's context menus.
///     <para>
///         It draws the theme's <see cref="ThemeRoles.FramePopup" /> part, else the flat frame. The width fits the widest
///         text, and is at least the width of the legacy menu. The menu stays inside its parent (the 640×480 root for a world
///         menu). The selected row
///         has the <see cref="ThemeRoles.RowHover" /> fill; the mouse or the Up and Down keys select a row. A click or
///         Enter picks the selected entry and closes the menu. A click that is not on an entry, a right-click and Escape
///         close the menu. The menu gets the keys when it is the top control and no text box has the focus; then it takes
///         Enter, also with no selection. While the chat box has the focus, the keys go to the chat.
///     </para>
///     <para>
///         The menu is on the control stack while it is open. So a host closes it on a click outside it, and also when the
///         host hides the menu's parent: a menu that stays on the stack takes the keys from the screen.
///     </para>
/// </summary>
public class PopupMenu : FramePanel
{
    /// <summary>The height of a row.</summary>
    public const int ROW_H = 14;

    //the space between the edge and the content in the flat frame: the 1 px border and a gap
    private const int PADDING = 4;

    //the space between the title row and the entries
    private const int TITLE_GAP = 2;

    //the width of a row in the legacy menu, which is the smallest width
    private const int MIN_ROW_W = 74;

    //the space before and after the text in a row
    private const int TEXT_PAD = 4;

    private readonly Action?[] Callbacks;
    private readonly UILabel[] EntryLabels;
    private readonly UILabel TitleLabel;

    private int EntryCount;
    private int FittedGeneration = -1;
    private int RowWidth = MIN_ROW_W;

    /// <param name="maxEntries">The largest number of entries that the menu shows.</param>
    public PopupMenu(int maxEntries)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxEntries);

        Visible = false;
        UseTheme = true;
        UsesControlStack = true;
        MaxEntries = maxEntries;
        Callbacks = new Action?[maxEntries];

        TitleLabel = new UILabel
        {
            Name = "title",
            Height = ROW_H,
            HorizontalAlignment = HorizontalAlignment.Center,
            PaddingLeft = 0,
            PaddingRight = 0,
            ShrinkToFit = false,
            IsHitTestVisible = false
        };
        TitleLabel.SetForegroundRole(ThemeRoles.TextTitle);
        AddChild(TitleLabel);

        EntryLabels = new UILabel[maxEntries];

        for (var i = 0; i < maxEntries; i++)
        {
            EntryLabels[i] = new UILabel
            {
                Name = $"entry_{i}",
                Height = ROW_H,
                PaddingLeft = TEXT_PAD,
                PaddingRight = TEXT_PAD,
                ShrinkToFit = false,
                IsHitTestVisible = false
            };
            EntryLabels[i].SetForegroundRole(ThemeRoles.Text);
            AddChild(EntryLabels[i]);
        }
    }

    protected override string FrameRole => ThemeRoles.FramePopup;

    /// <summary>The largest number of entries that the menu shows.</summary>
    public int MaxEntries { get; }

    /// <summary>The selected entry, or -1 for none. Internal for the tests.</summary>
    internal int SelectedIndex { get; private set; } = -1;

    /// <summary>The number of entries that the menu shows now.</summary>
    public int Count => EntryCount;

    //the distance from the edge to the rows
    private int Inset => InsetFor(PADDING);

    //the top of the first entry row
    private int EntriesTop => Inset + ROW_H + TITLE_GAP;

    /// <summary>
    ///     Opens the menu with its top left corner at (<paramref name="x" />, <paramref name="y" />), moved in when it would
    ///     go out of the viewport. Entries after <see cref="MaxEntries" /> are dropped. With no entry, the menu does not open.
    /// </summary>
    public void Show(int x, int y, string title, IReadOnlyList<(string Label, Action Callback)> entries)
    {
        if (entries.Count == 0)
            return;

        EntryCount = Math.Min(entries.Count, MaxEntries);
        TitleLabel.Text = title;

        for (var i = 0; i < MaxEntries; i++)
        {
            var shown = i < EntryCount;

            EntryLabels[i].Text = shown ? entries[i].Label : string.Empty;
            EntryLabels[i].Visible = shown;
            Callbacks[i] = shown ? entries[i].Callback : null;
        }

        Layout();
        PlaceAt(x, y);

        SelectedIndex = -1;

        //a second Show while the menu is open keeps one entry on the control stack
        if (!Visible)
            InputDispatcher.Instance?.PushControl(this);

        Visible = true;
    }

    /// <summary>Closes the menu. Does nothing when the menu is not open.</summary>
    public void Hide()
    {
        if (!Visible)
            return;

        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
        SelectedIndex = -1;
    }

    //puts the top left corner at (x, y) in the parent, moved in so that the menu stays inside the parent
    private void PlaceAt(int x, int y)
    {
        var (width, height) = Parent is { } parent ? (parent.Width, parent.Height) : (ChaosGame.VIRTUAL_WIDTH, ChaosGame.VIRTUAL_HEIGHT);

        X = Math.Clamp(x, 0, Math.Max(0, width - Width));
        Y = Math.Clamp(y, 0, Math.Max(0, height - Height));
    }

    //fits the width to the widest text at the current face, and places the rows
    private void Layout()
    {
        var widest = TextRenderer.MeasureWidth(TitleLabel.Text);

        for (var i = 0; i < EntryCount; i++)
            widest = Math.Max(widest, TextRenderer.MeasureWidth(EntryLabels[i].Text));

        //1 px more than the text, so that a rounding difference does not clip the last glyph
        RowWidth = Math.Max(MIN_ROW_W, widest + 2 * TEXT_PAD + 1);
        FittedGeneration = FontEngine.Instance.Generation;

        var inset = Inset;

        TitleLabel.X = inset;
        TitleLabel.Y = inset;
        TitleLabel.Width = RowWidth;

        for (var i = 0; i < MaxEntries; i++)
        {
            EntryLabels[i].X = inset;
            EntryLabels[i].Y = EntriesTop + i * ROW_H;
            EntryLabels[i].Width = RowWidth;
        }

        Width = 2 * inset + RowWidth;
        Height = EntriesTop + EntryCount * ROW_H + inset;
    }

    /// <summary>The bounds of entry row <paramref name="index" /> in panel coordinates. Internal for the tests.</summary>
    internal Rectangle RowBounds(int index) => new(Inset, EntriesTop + index * ROW_H, RowWidth, ROW_H);

    /// <summary>The entry row at a point in panel coordinates, or -1. Internal for the tests.</summary>
    internal int RowAt(int localX, int localY)
    {
        var inset = Inset;

        if ((localX < inset) || (localX >= inset + RowWidth) || (localY < EntriesTop))
            return -1;

        var index = (localY - EntriesTop) / ROW_H;

        return index < EntryCount ? index : -1;
    }

    //picks an entry: closes the menu, then runs the entry
    private void Pick(int index)
    {
        var callback = Callbacks[index];

        Hide();
        callback?.Invoke();
    }

    public override void Update(GameTime gameTime)
    {
        //a new face or window size changes the text widths
        if (Visible && (FontEngine.Instance.Generation != FittedGeneration))
        {
            Layout();
            PlaceAt(X, Y);
        }

        base.Update(gameTime);
    }

    //the selected row's fill lies on the frame and under the text
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        base.DrawChrome(spriteBatch, bounds);

        if (SelectedIndex < 0)
            return;

        var row = RowBounds(SelectedIndex);
        row.Offset(ScreenX, ScreenY);
        DrawRectClipped(spriteBatch, row, ThemeColor(ThemeRoles.RowHover));
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                Hide();
                e.Handled = true;

                break;
            case Keys.Up when EntryCount > 0:
                SelectedIndex = SelectedIndex <= 0 ? EntryCount - 1 : SelectedIndex - 1;
                e.Handled = true;

                break;
            case Keys.Down when EntryCount > 0:
                SelectedIndex = (SelectedIndex + 1) % EntryCount;
                e.Handled = true;

                break;

            //Alt+Enter changes the window size; it must not pick an entry
            case Keys.Enter when !e.Alt:
                if (SelectedIndex >= 0)
                    Pick(SelectedIndex);

                e.Handled = true;

                break;
        }
    }

    public override void OnMouseMove(MouseMoveEvent e) => SelectedIndex = RowAt(e.ScreenX - ScreenX, e.ScreenY - ScreenY);

    public override void OnMouseLeave() => SelectedIndex = -1;

    //a press on the menu is the menu's, so that a right-press does not walk the player to the tile under it
    public override void OnMouseDown(MouseDownEvent e) => e.Handled = true;

    public override void OnClick(ClickEvent e)
    {
        var index = e.Button == MouseButton.Right ? -1 : RowAt(e.ScreenX - ScreenX, e.ScreenY - ScreenY);

        if (index >= 0)
            Pick(index);
        else
            Hide();

        e.Handled = true;
    }
}
