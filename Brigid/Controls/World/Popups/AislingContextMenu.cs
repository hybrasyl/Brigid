#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     A popup menu that appears when Ctrl+clicking a non-self aisling. Uses popupbox.epf as background. Top row shows the
///     player's name, bottom 3 rows are clickable options: info, group request, whisper.
/// </summary>
public sealed class AislingContextMenu : UIPanel
{
    private const string EPF_FILE = "popupbox.epf";
    private const int BOX_X = 4;
    private const int BOX_WIDTH = 74;
    private const int BOX_HEIGHT = 14;
    private const int BOX_START_Y = 6;
    private const int OPTIONS_OFFSET_Y = 2;

    private static readonly Color BOX_FILL = new Color(68, 68, 68) * (140 / 255f);
    private static readonly Color BOX_HOVER = new Color(187, 187, 187) * (160 / 255f);

    private static readonly string[] OPTION_LABELS =
    [
        "info",
        "group request",
        "whisper"
    ];

    private readonly UIImage FrameImage;
    private readonly UIPanel HoverOverlay;
    private readonly UILabel NameLabel;
    private readonly Action[] OptionCallbacks = new Action[3];
    private readonly UILabel[] OptionLabels;
    private int HoveredIndex = -1;

    public AislingContextMenu()
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

        NameLabel = new UILabel
        {
            Name = "Name",
            X = BOX_X,
            Y = BOX_START_Y,
            Width = BOX_WIDTH,
            Height = BOX_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            PaddingLeft = 0,
            ShrinkToFit = false
        };

        AddChild(NameLabel);

        OptionLabels = new UILabel[3];

        for (var i = 0; i < 3; i++)
        {
            OptionLabels[i] = new UILabel
            {
                Name = $"Option{i}",
                X = BOX_X,
                Y = BOX_START_Y + OPTIONS_OFFSET_Y + (i + 1) * BOX_HEIGHT,
                Width = BOX_WIDTH,
                Height = BOX_HEIGHT,
                Text = OPTION_LABELS[i],
                PaddingLeft = 0,
                ForegroundColor = Color.White,
                ShrinkToFit = false
            };

            AddChild(OptionLabels[i]);
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

    public void Show(
        int screenX,
        int screenY,
        string name,
        Action onInfo,
        Action onGroupRequest,
        Action onWhisper)
    {
        var bg = GetFrameTexture();

        if (bg is null)
            return;

        NameLabel.Text = name;
        NameLabel.ForegroundColor = Color.White;
        OptionCallbacks[0] = onInfo;
        OptionCallbacks[1] = onGroupRequest;
        OptionCallbacks[2] = onWhisper;

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
            HoveredIndex = index is >= 0 and < 3 ? index : -1;
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

        if (HoveredIndex is >= 0 and < 3)
        {
            OptionCallbacks[HoveredIndex]();
            Hide();
        } else
            Hide();

        e.Handled = true;
    }

}
