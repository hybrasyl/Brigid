#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     A popup menu that appears when Alt+right-clicking near one or more door tiles. Uses popupbox.epf as background
///     (same visual as <see cref="AislingContextMenu" />). Top row is a static "Doors" title, bottom 3 rows are the
///     nearby-door entries populated dynamically per <see cref="Show" /> call. Entries are configured from outside —
///     label text ("Open Door" / "Close Door") and callback (send a ClickTile packet to the door's coords) both come
///     from the caller that performed the proximity scan.
/// </summary>
public sealed class DoorContextMenu : UIPanel
{
    public const int MAX_ENTRIES = 3;

    private const string EPF_FILE = "popupbox.epf";
    private const string TITLE_TEXT = "Doors";
    private const int BOX_X = 4;
    private const int BOX_WIDTH = 74;
    private const int BOX_HEIGHT = 14;
    private const int BOX_START_Y = 6;
    private const int OPTIONS_OFFSET_Y = 2;

    private static readonly Color BOX_FILL = new Color(68, 68, 68) * (140 / 255f);
    private static readonly Color BOX_HOVER = new Color(187, 187, 187) * (160 / 255f);

    private readonly UIImage FrameImage;
    private readonly UIPanel HoverOverlay;
    private readonly UILabel TitleLabel;
    private readonly Action?[] EntryCallbacks = new Action?[MAX_ENTRIES];
    private readonly UILabel[] EntryLabels;
    private int ActiveEntryCount;
    private int HoveredIndex = -1;

    public DoorContextMenu()
    {
        Visible = false;
        UsesControlStack = true;
        BackgroundColor = BOX_FILL;

        HoverOverlay = new UIPanel
        {
            Name = "HoverOverlay",
            BackgroundColor = BOX_HOVER,
            Visible = false,
            IsHitTestVisible = false,
            X = BOX_X,
            Width = BOX_WIDTH,
            Height = BOX_HEIGHT
        };

        AddChild(HoverOverlay);

        FrameImage = new UIImage
        {
            Name = "Frame",
            X = 0,
            Y = 0,
            IsHitTestVisible = false
        };

        AddChild(FrameImage);

        TitleLabel = new UILabel
        {
            Name = "Title",
            X = BOX_X,
            Y = BOX_START_Y,
            Width = BOX_WIDTH,
            Height = BOX_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            PaddingLeft = 0,
            ShrinkToFit = false,
            Text = TITLE_TEXT,
            ForegroundColor = Color.White
        };

        AddChild(TitleLabel);

        EntryLabels = new UILabel[MAX_ENTRIES];

        for (var i = 0; i < MAX_ENTRIES; i++)
        {
            EntryLabels[i] = new UILabel
            {
                Name = $"Entry{i}",
                X = BOX_X,
                Y = BOX_START_Y + OPTIONS_OFFSET_Y + (i + 1) * BOX_HEIGHT,
                Width = BOX_WIDTH,
                Height = BOX_HEIGHT,
                Text = string.Empty,
                PaddingLeft = 0,
                ForegroundColor = Color.White,
                ShrinkToFit = false
            };

            AddChild(EntryLabels[i]);
        }
    }

    private Texture2D? GetFrameTexture()
    {
        if (FrameImage.Texture is not null)
            return FrameImage.Texture;

        if (UiRenderer.Instance is null)
            return null;

        var frameCount = UiRenderer.Instance.GetEpfFrameCount(EPF_FILE);

        if (frameCount <= 0)
            return null;

        var texture = UiRenderer.Instance.GetEpfTexture(EPF_FILE, 0);
        FrameImage.Texture = texture;
        FrameImage.Width = texture.Width;
        FrameImage.Height = texture.Height;
        Width = texture.Width;
        Height = texture.Height;

        return texture;
    }

    public void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);
        Visible = false;
        HoveredIndex = -1;
    }

    /// <summary>
    ///     Shows the menu at (screenX, screenY) with up to <see cref="MAX_ENTRIES" /> entries. Extra entries beyond the
    ///     limit are dropped; the caller is expected to pre-sort by proximity.
    /// </summary>
    public void Show(int screenX, int screenY, IReadOnlyList<(string Label, Action Callback)> entries)
    {
        var bg = GetFrameTexture();

        if (bg is null || entries.Count == 0)
            return;

        ActiveEntryCount = Math.Min(entries.Count, MAX_ENTRIES);

        for (var i = 0; i < MAX_ENTRIES; i++)
            if (i < ActiveEntryCount)
            {
                EntryLabels[i].Text = entries[i].Label;
                EntryCallbacks[i] = entries[i].Callback;
            } else
            {
                EntryLabels[i].Text = string.Empty;
                EntryCallbacks[i] = null;
            }

        X = screenX;
        Y = screenY;

        //clamp to screen bounds
        if ((X + Width) > ChaosGame.VIRTUAL_WIDTH)
            X = ChaosGame.VIRTUAL_WIDTH - Width;

        if ((Y + Height) > ChaosGame.VIRTUAL_HEIGHT)
            Y = ChaosGame.VIRTUAL_HEIGHT - Height;

        HoveredIndex = -1;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            e.Handled = true;
        }
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        var localX = e.ScreenX - ScreenX;
        var localY = e.ScreenY - ScreenY;
        var optionsStartY = BOX_START_Y + OPTIONS_OFFSET_Y + BOX_HEIGHT;

        if (localX is >= BOX_X and < BOX_X + BOX_WIDTH && (localY >= optionsStartY))
        {
            var index = (localY - optionsStartY) / BOX_HEIGHT;
            HoveredIndex = index >= 0 && index < ActiveEntryCount ? index : -1;
        } else
            HoveredIndex = -1;

        if (HoveredIndex >= 0)
        {
            HoverOverlay.Y = BOX_START_Y + OPTIONS_OFFSET_Y + (HoveredIndex + 1) * BOX_HEIGHT;
            HoverOverlay.Visible = true;
        } else
            HoverOverlay.Visible = false;
    }

    public override void OnMouseLeave()
    {
        HoveredIndex = -1;
        HoverOverlay.Visible = false;
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button == MouseButton.Right)
        {
            Hide();
            e.Handled = true;

            return;
        }

        if (HoveredIndex >= 0 && HoveredIndex < ActiveEntryCount)
        {
            EntryCallbacks[HoveredIndex]?.Invoke();
            Hide();
        } else
            Hide();

        e.Handled = true;
    }
}
