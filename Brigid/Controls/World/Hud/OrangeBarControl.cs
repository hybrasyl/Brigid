#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Data.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.Hud;

/// <summary>
///     Orange bar message display above inventory. Shows the latest server message; supports drag-expand to reveal
///     history. Background from SystemMessagePane (_nsmbk.spf).
/// </summary>
public sealed class OrangeBarControl : UIPanel
{
    private const int MAX_EXPAND_LINES = 10;
    private const int GLYPH_HEIGHT = 12;

    //nudge the message lines up so they sit centered on the orange bar groove rather than low against its lower lip.
    private const int LINE_Y_NUDGE = 2;
    private readonly UILabel[] Lines;
    private readonly Texture2D? PaneBg;

    private readonly Rectangle TextBounds;
    private int DragMouseStartY;
    private int ExpandedLines;
    private Rectangle WrapBounds;

    public bool IsDragging { get; private set; }

    public OrangeBarControl(ControlPrefabSet hudPrefabSet)
    {
        Name = "OrangeBar";

        TextBounds = PrefabPanel.GetRect(hudPrefabSet, "SystemMessage");
        WrapBounds = PrefabPanel.GetRect(hudPrefabSet, "SystemMessageWrap");

        if (hudPrefabSet.Contains("SystemMessagePane"))
            PaneBg = UiRenderer.Instance!.GetPrefabTexture(hudPrefabSet.Name, "SystemMessagePane", 0);

        X = WrapBounds.X;
        Y = WrapBounds.Y;
        Width = WrapBounds.Width;
        Height = WrapBounds.Height;

        Lines = new UILabel[MAX_EXPAND_LINES];

        for (var i = 0; i < MAX_EXPAND_LINES; i++)
        {
            Lines[i] = new UILabel
            {
                Name = $"OrangeLine{i}",
                X = TextBounds.X - WrapBounds.X,
                Width = TextBounds.Width,
                Height = GLYPH_HEIGHT,
                PaddingLeft = 0,
                PaddingTop = 0,
                Visible = false,
                IsHitTestVisible = false
            };

            AddChild(Lines[i]);
        }
    }

    public override void Dispose()
    {
        PaneBg?.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible || PaneBg is null)
            return;

        var sx = Parent?.ScreenX ?? 0;
        var sy = Parent?.ScreenY ?? 0;

        var paneX = sx + WrapBounds.X;
        var topY = sy + WrapBounds.Y;
        var expandY = sy + TextBounds.Y + GLYPH_HEIGHT;
        var paneWidth = WrapBounds.Width;
        var contentHeight = ExpandedLines * GLYPH_HEIGHT;
        var totalHeight = expandY - topY + contentHeight;

        //expand bounds to cover expanded content so ClipRect doesn't clip it
        Height = totalHeight + 4;
        UpdateClipRect();

        //solid opaque fill
        DrawRectClipped(
            spriteBatch,
            new Rectangle(
                paneX,
                topY,
                paneWidth,
                totalHeight),
            Color.Black);

        //reveal expand texture (1:1, no stretching)
        var revealHeight = Math.Min(totalHeight, PaneBg.Height - 4);

        DrawTexture(
            spriteBatch,
            PaneBg,
            new Vector2(paneX, topY),
            new Rectangle(
                0,
                0,
                PaneBg.Width,
                revealHeight),
            Color.White);

        //bottom 4px edge
        var srcY = PaneBg.Height - 4;

        DrawTexture(
            spriteBatch,
            PaneBg,
            new Vector2(paneX, topY + totalHeight),
            new Rectangle(
                0,
                srcY,
                PaneBg.Width,
                4),
            Color.White);

        //update and position line labels
        RefreshLines();

        //children (line labels) drawn by base
        base.Draw(spriteBatch);
    }

    private void RefreshLines()
    {
        var history = WorldState.Chat.GetOrangeBarHistory();

        if (history.Count == 0)
        {
            for (var i = 0; i < MAX_EXPAND_LINES; i++)
                Lines[i].Visible = false;

            return;
        }

        //textbounds.y is relative to the hud parent, wrapbounds.y is our own y — compute relative offset
        var baseRelY = TextBounds.Y - WrapBounds.Y - LINE_Y_NUDGE;
        var slot = 0;

        for (var i = history.Count - 1; (i >= 0) && (slot <= ExpandedLines); i--)
        {
            var msg = history[i];
            var lineY = baseRelY + ExpandedLines * GLYPH_HEIGHT - slot * GLYPH_HEIGHT;
            Lines[slot].Y = lineY;
            Lines[slot].Text = msg.Text;
            Lines[slot].ForegroundColor = msg.Color;
            Lines[slot].Visible = true;
            slot++;
        }

        for (; slot < MAX_EXPAND_LINES; slot++)
            Lines[slot].Visible = false;
    }

    public override void OnMouseDown(MouseDownEvent e)
    {
        if ((e.Button != MouseButton.Left) || PaneBg is null || (WrapBounds == Rectangle.Empty))
            return;

        IsDragging = true;
        DragMouseStartY = e.ScreenY;
        ExpandedLines = 0;
        e.Handled = true;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        if (!IsDragging)
            return;

        var dragPixels = e.ScreenY - DragMouseStartY;
        ExpandedLines = Math.Clamp(dragPixels / GLYPH_HEIGHT, 0, MAX_EXPAND_LINES - 1);
    }

    public override void OnMouseUp(MouseUpEvent e)
    {
        if ((e.Button == MouseButton.Left) && IsDragging)
        {
            IsDragging = false;
            ExpandedLines = 0;
        }
    }
}
