#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Brigid.Data;
using Brigid.Extensions;
using DALib.Drawing;
using DALib.Drawing.Virtualized;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using SkiaSharp;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     Notepad popup with a 9-slice tiled background from line001.epf. Supports editable mode (multi-line UITextBox) and
///     readonly mode (word-wrapped UILabel). Escape closes; editable mode sends text on close.
/// </summary>
public sealed class NotepadControl : UIPanel
{
    private const int TILE_SIZE = 16;
    private const int MAX_MESSAGE_LENGTH = 3500;

    //notepad type frames start at frame 48 in line001.epf
    private const int NOTEPAD_BASE_FRAME = 48;

    //9-slice piece indices within each type's 9-frame group
    //standard clockwise: tl, top, tr, right, br, bottom, bl, left, fill
    private const int PIECE_COUNT = 9;
    private const int PIECE_TL = 0;
    private const int PIECE_TOP = 1;
    private const int PIECE_TR = 2;
    private const int PIECE_RIGHT = 3;
    private const int PIECE_BR = 4;
    private const int PIECE_BOTTOM = 5;
    private const int PIECE_BL = 6;
    private const int PIECE_LEFT = 7;
    private const int PIECE_FILL = 8;

    private static SKImage[]? CachedEpfFrames;

    private readonly UITextBox ContentBox;
    private readonly UILabel ReadonlyLabel;

    //bare scroll model (no visible bar) driving the readonly label — the editable box scrolls itself
    private readonly ScrollModel ReadonlyScroll = new();
    private readonly LabelScrollSource ReadonlySource;
    private byte EditSlot;
    private bool IsEditable;

    public NotepadControl()
    {
        Name = "Notepad";
        Visible = false;
        UsesControlStack = true;

        ContentBox = new UITextBox
        {
            Name = "NotepadContent",
            IsMultiLine = true,
            MaxLength = MAX_MESSAGE_LENGTH,
            IsReadOnly = false,
            IsSelectable = true,
            ForegroundColor = Color.Black,
            PaddingLeft = 2,
            PaddingRight = 2,
            PaddingTop = 2,
            PaddingBottom = 2,
            Visible = false,
            ZIndex = 1
        };
        AddChild(ContentBox);

        ReadonlyLabel = new UILabel
        {
            Name = "NotepadReadonly",
            WordWrap = true,
            VerticalAlignment = VerticalAlignment.Top,
            ForegroundColor = Color.Black,
            PaddingLeft = 2,
            PaddingTop = 2,
            Visible = false
        };
        AddChild(ReadonlyLabel);

        ReadonlySource = new LabelScrollSource(ReadonlyLabel);
        ReadonlyScroll.Changed += ReadonlySource.ApplyOffset;
    }

    public void Close()
    {
        if (IsEditable)
        {
            var text = ContentBox.Text;

            if (text.Length > MAX_MESSAGE_LENGTH)
                text = text[..MAX_MESSAGE_LENGTH];

            //convert newlines back to tab for the protocol
            text = text.Replace('\n', '\t');

            OnSave?.Invoke(EditSlot, text);
        }

        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
        ContentBox.IsFocused = false;
        ContentBox.Visible = false;
        ReadonlyLabel.Visible = false;
    }

    private void ComposeBackground(byte notepadType)
    {
        //dispose any previous background
        Background?.Dispose();
        Background = null;

        var frames = GetEpfFrames();

        if (frames is null)
            return;

        var baseFrame = NOTEPAD_BASE_FRAME + notepadType * PIECE_COUNT;

        //load the 9 pieces, skipping any that fall outside the epf frame count
        var pieces = new SKImage?[PIECE_COUNT];

        for (var i = 0; i < PIECE_COUNT; i++)
        {
            var frameIndex = baseFrame + i;

            if (frameIndex < frames.Length)
                pieces[i] = frames[frameIndex];
        }

        var info = new SKImageInfo(
            Width,
            Height,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);
        using var surface = SKSurface.Create(info);

        if (surface is null)
            return;

        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);

        //fill: tile across the inset area
        TilePiece(
            canvas,
            pieces[PIECE_FILL],
            TILE_SIZE,
            TILE_SIZE,
            Width - TILE_SIZE * 2,
            Height - TILE_SIZE * 2);

        //draw order (back to front): top -> bottom -> left -> right -> tl -> tr -> bl -> br
        //edges first (tiled), then corners on top
        TilePieceH(
            canvas,
            pieces[PIECE_TOP],
            TILE_SIZE,
            0,
            Width - TILE_SIZE * 2);

        TilePieceH(
            canvas,
            pieces[PIECE_BOTTOM],
            TILE_SIZE,
            Height - TILE_SIZE,
            Width - TILE_SIZE * 2);

        TilePieceV(
            canvas,
            pieces[PIECE_LEFT],
            0,
            TILE_SIZE,
            Height - TILE_SIZE * 2);

        TilePieceV(
            canvas,
            pieces[PIECE_RIGHT],
            Width - TILE_SIZE,
            TILE_SIZE,
            Height - TILE_SIZE * 2);

        //corners (drawn last, on top of edges)
        DrawPiece(
            canvas,
            pieces[PIECE_TL],
            0,
            0);

        DrawPiece(
            canvas,
            pieces[PIECE_TR],
            Width - TILE_SIZE,
            0);

        DrawPiece(
            canvas,
            pieces[PIECE_BL],
            0,
            Height - TILE_SIZE);

        DrawPiece(
            canvas,
            pieces[PIECE_BR],
            Width - TILE_SIZE,
            Height - TILE_SIZE);

        using var snapshot = surface.Snapshot();
        Background = TextureConverter.ToTexture2D(snapshot);
    }

    private void ConfigureSize(byte notepadType, byte width, byte height)
    {
        //exact sizing formula: (field + offset) * 16
        var pixelWidth = (width + 2) * TILE_SIZE;
        var pixelHeight = (height + 3) * TILE_SIZE;

        Width = pixelWidth;
        Height = pixelHeight;

        this.CenterOnScreen();

        //compose the 9-slice background
        ComposeBackground(notepadType);

        //text area: left=16, top=26, right=w-16, bottom=height*16+36
        var contentX = TILE_SIZE;
        var contentY = 26;
        var contentWidth = pixelWidth - TILE_SIZE * 2;
        var contentHeight = height * TILE_SIZE + 36 - contentY;

        ContentBox.X = contentX;
        ContentBox.Y = contentY;
        ContentBox.Width = contentWidth;
        ContentBox.Height = contentHeight;

        ReadonlyLabel.X = contentX;
        ReadonlyLabel.Y = contentY;
        ReadonlyLabel.Width = contentWidth;
        ReadonlyLabel.Height = contentHeight;
    }

    public override void Dispose()
    {
        ContentBox.IsFocused = false;

        base.Dispose();
    }

    private static void DrawPiece(
        SKCanvas canvas,
        SKImage? piece,
        int x,
        int y)
    {
        if (piece is not null)
            canvas.DrawImage(piece, x, y);
    }

    private static SKImage[]? GetEpfFrames()
    {
        if (CachedEpfFrames is not null)
            return CachedEpfFrames;

        if (!DatArchives.Legend.TryGetValue("line001.epf", out var entry)
            || !DatArchives.Legend.TryGetValue("legend.pal", out var palEntry))
            return null;

        var palette = Palette.FromEntry(palEntry);

        var epf = EpfView.FromEntry(entry);
        var frames = new SKImage[epf.Count];

        for (var i = 0; i < epf.Count; i++)
            frames[i] = Graphics.RenderImage(epf[i], palette);

        CachedEpfFrames = frames;

        return CachedEpfFrames;
    }

    private static string NormalizeMessage(string message)
    {
        if (string.IsNullOrEmpty(message))
            return string.Empty;

        //protocol uses tab (0x09) as line separator; convert to newlines for display
        return message.Replace('\t', '\n');
    }

    /// <summary>
    ///     Fired when the editable notepad is closed. Parameters: slot, message text.
    /// </summary>
    public event NotepadSavedHandler? OnSave;

    /// <summary>
    ///     Shows the notepad in editable mode with the given message text and sizing hints.
    /// </summary>
    public void ShowEditable(
        byte slot,
        byte notepadType,
        byte width,
        byte height,
        string message)
    {
        EditSlot = slot;
        IsEditable = true;
        ConfigureSize(notepadType, width, height);

        ContentBox.Text = NormalizeMessage(message);
        ContentBox.CursorPosition = 0;
        ContentBox.ScrollOffset = 0;
        ContentBox.Visible = true;
        ContentBox.IsFocused = true;

        ReadonlyLabel.Visible = false;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>
    ///     Shows the notepad in readonly mode.
    /// </summary>
    public void ShowReadonly(
        byte notepadType,
        byte width,
        byte height,
        string message)
    {
        IsEditable = false;
        ConfigureSize(notepadType, width, height);

        ReadonlyLabel.Text = NormalizeMessage(message);
        ReadonlyLabel.Visible = true;
        ReadonlyScroll.Configure(ReadonlySource);
        ReadonlyScroll.ScrollToStart();

        ContentBox.Visible = false;
        ContentBox.IsFocused = false;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    /// <summary>
    ///     Tiles a piece in both directions across the given area.
    /// </summary>
    private static void TilePiece(
        SKCanvas canvas,
        SKImage? piece,
        int startX,
        int startY,
        int areaWidth,
        int areaHeight)
    {
        if (piece is null)
            return;

        for (var x = startX; x < (startX + areaWidth); x += TILE_SIZE)
            for (var y = startY; y < (startY + areaHeight); y += TILE_SIZE)
                canvas.DrawImage(piece, x, y);
    }

    /// <summary>
    ///     Tiles a piece horizontally.
    /// </summary>
    private static void TilePieceH(
        SKCanvas canvas,
        SKImage? piece,
        int startX,
        int y,
        int areaWidth)
    {
        if (piece is null)
            return;

        for (var x = startX; x < (startX + areaWidth); x += TILE_SIZE)
            canvas.DrawImage(piece, x, y);
    }

    /// <summary>
    ///     Tiles a piece vertically.
    /// </summary>
    private static void TilePieceV(
        SKCanvas canvas,
        SKImage? piece,
        int x,
        int startY,
        int areaHeight)
    {
        if (piece is null)
            return;

        for (var y = startY; y < (startY + areaHeight); y += TILE_SIZE)
            canvas.DrawImage(piece, x, y);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        //editable mode: the UITextBox handles its own wheel; readonly: drive the bare model (line-granular)
        if (IsEditable || !ReadonlyScroll.CanScroll)
            return;

        ReadonlyScroll.WheelBy(e.Delta);
        e.Handled = true;
    }
}