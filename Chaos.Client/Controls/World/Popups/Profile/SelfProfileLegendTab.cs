#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Controls.Generic;
using Chaos.Client.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Chaos.Client.Controls.World.Popups.Profile;

/// <summary>
///     Legend tab page (_nui_dr). Displays legend mark entries with icons and colored text.
///     Exactly 12 rows visible, with scroll support.
/// </summary>
public sealed class SelfProfileLegendTab : PrefabPanel
{
    private const int MAX_VISIBLE_ROWS = 12;

    private readonly Texture2D[] IconFrames;
    private readonly Rectangle LegendListRect;
    private readonly int RowHeight;
    private readonly LegendMarkControl[] Rows;
    private readonly ScrollBarControl ScrollBar;

    private int DataVersion;
    private List<LegendMarkEntry> Marks = [];
    private int RenderedVersion = -1;
    private int ScrollOffset;

    public SelfProfileLegendTab(string prefabName)
        : base(prefabName, false)
    {
        Name = prefabName;
        Visible = false;

        LegendListRect = GetRect("LegendList");

        if (LegendListRect == Rectangle.Empty)
            LegendListRect = new Rectangle(
                38,
                33,
                524,
                237);

        RowHeight = LegendListRect.Height / MAX_VISIBLE_ROWS;

        //create row controls
        Rows = new LegendMarkControl[MAX_VISIBLE_ROWS];

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            Rows[i] = new LegendMarkControl
            {
                Name = $"LegendRow{i}",
                X = LegendListRect.X,
                Y = LegendListRect.Y + i * (RowHeight + 1),
                Width = LegendListRect.Width,
                Height = RowHeight
            };

            AddChild(Rows[i]);
        }

        //scrollbar on the right side of the legend list
        ScrollBar = new ScrollBarControl
        {
            Name = "LegendScrollBar",
            X = LegendListRect.X + LegendListRect.Width - 16,
            Y = LegendListRect.Y,
            Height = LegendListRect.Height,
            VisibleItems = MAX_VISIBLE_ROWS
        };

        ScrollBar.OnValueChanged += v =>
        {
            ScrollOffset = v;
            DataVersion++;
        };

        AddChild(ScrollBar);

        //legend mark icons — pack-first with legacy legends.epf fallback. Clamp to byte range since mark.Icon is byte.
        var cache = UiRenderer.Instance!;
        var frameCount = Math.Min(cache.GetEpfFrameCount("legends.epf"), 256);
        IconFrames = new Texture2D[frameCount];

        for (var i = 0; i < frameCount; i++)
            IconFrames[i] = cache.GetLegendMarkIcon((byte)i);
    }

    public override void Dispose()
    {
        foreach (var t in IconFrames)
            t.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        RefreshRows();
        base.Draw(spriteBatch);
    }

    private void RefreshRows()
    {
        if (RenderedVersion == DataVersion)
            return;

        RenderedVersion = DataVersion;

        for (var i = 0; i < MAX_VISIBLE_ROWS; i++)
        {
            var markIndex = ScrollOffset + i;

            if (markIndex < Marks.Count)
            {
                var mark = Marks[markIndex];
                var icon = mark.Icon < IconFrames.Length ? IconFrames[mark.Icon] : null;
                var iconWidth = icon?.Width ?? 21;
                var iconHeight = icon?.Height ?? 20;

                Rows[i]
                    .SetMark(
                        icon,
                        mark.Text,
                        mark.Color,
                        iconWidth,
                        iconHeight);
                Rows[i].Visible = true;
            } else
            {
                Rows[i]
                    .Clear();
                Rows[i].Visible = false;
            }
        }
    }

    /// <summary>
    ///     Sets the legend mark entries to display.
    /// </summary>
    public void SetMarks(List<LegendMarkEntry> marks)
    {
        Marks = marks;
        ScrollOffset = 0;
        ScrollBar.Value = 0;
        ScrollBar.TotalItems = marks.Count;
        ScrollBar.MaxValue = Math.Max(0, marks.Count - MAX_VISIBLE_ROWS);
        DataVersion++;
    }

    public override void OnMouseScroll(MouseScrollEvent e)
    {
        if (Marks.Count > MAX_VISIBLE_ROWS)
        {
            ScrollOffset = Math.Clamp(ScrollOffset - e.Delta, 0, Marks.Count - MAX_VISIBLE_ROWS);
            ScrollBar.Value = ScrollOffset;
            DataVersion++;
            e.Handled = true;
        }
    }
}