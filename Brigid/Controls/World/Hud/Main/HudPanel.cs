#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Hud.Main;

/// <summary>
///     The panel of a window of the new HUD (BRIG-127 D30): a framed box with a title and an X button, which
///     <see cref="HudPanelHost" /> places. A window that a later card makes shows its title and a line that says it is not
///     built yet; that card puts its content in the panel. A compact panel has a swap button beside the X, and a window with
///     a <see cref="HudWindow.TitleLink" /> has a button at the left end of the title row that opens that window (the host
///     binds it). The panel and its content have one size with or without a
///     theme: the theme fits the frame, and the frame does not fit the theme (Sabrael, 2026-11-18). The border of the
///     frame art of a theme must fit in <see cref="PADDING" />; a wider border lies over the content.
/// </summary>
internal sealed class HudPanel : FramePanel
{
    public const int PADDING = 6;
    private const int TITLE_HEIGHT = 16;
    private const int CLOSE_SIZE = 14;
    private const int BUTTON_GAP = 2;

    //the title keeps the place of the two buttons clear on both sides, so it stays centred on every panel
    private const int TITLE_CLEAR = 2 * CLOSE_SIZE + BUTTON_GAP;

    public HudPanel(HudWindow window)
    {
        Name = $"{window.Id}_panel";
        Window = window;
        Visible = false;
        UseTheme = true;
        SetBounds(window.Panel == HudPanelSize.Large ? MainHudLayout.LargePanel : MainHudLayout.CompactRight);

        const int inset = PADDING;

        var title = new UILabel
        {
            Name = "title",
            Text = window.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        //the title is centred on the panel, as in the wireframe
        title.SetBounds(new Rectangle(inset + TITLE_CLEAR, inset, Width - 2 * (inset + TITLE_CLEAR), TITLE_HEIGHT));
        title.SetForegroundRole(ThemeRoles.TextTitle);
        AddChild(title);

        var close = new TextButton("X", CLOSE_SIZE, CLOSE_SIZE)
        {
            Name = "close",
            X = Width - inset - CLOSE_SIZE,
            Y = inset + (TITLE_HEIGHT - CLOSE_SIZE) / 2
        };

        close.Clicked += () => CloseRequested?.Invoke(this);
        AddChild(close);

        //a compact panel changes sides with the other compact panel (BRIG-150); a large panel has no sides
        if (window.Panel == HudPanelSize.Compact)
        {
            var swap = new IconButton(ThemeRoles.IconPanelSwapSides, new Rectangle(close.X - BUTTON_GAP - CLOSE_SIZE, close.Y, CLOSE_SIZE, CLOSE_SIZE))
            {
                Name = "swap_sides"
            };

            swap.Clicked += () => SwapSidesRequested?.Invoke();
            AddChild(swap);
        }

        //the left end of the title row is clear as the right end is, so a link fits there and the title stays centred
        if (window.TitleLink is { } link)
        {
            LinkButton = new IconButton(link.LinkRole, new Rectangle(inset, close.Y, CLOSE_SIZE, CLOSE_SIZE))
            {
                Name = $"link_{link.Id}"
            };

            AddChild(LinkButton);
        }

        ContentBounds = new Rectangle(inset, inset + TITLE_HEIGHT, Width - 2 * inset, Height - 2 * inset - TITLE_HEIGHT);

        var notBuilt = new UILabel
        {
            Name = "not_built",
            Text = "This window is not built yet.",
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };

        notBuilt.SetBounds(ContentBounds);
        notBuilt.SetForegroundRole(ThemeRoles.TextDisabled);
        AddChild(notBuilt);
    }

    public HudWindow Window { get; }

    /// <summary>The button that opens <see cref="HudWindow.TitleLink" />, or null; the host binds it.</summary>
    public IconButton? LinkButton { get; }

    /// <summary>The place of the content, in the panel: under the title, inside the frame.</summary>
    public Rectangle ContentBounds { get; }

    /// <summary>Puts a window card's content in the panel at <see cref="ContentBounds" />, in place of the not-built line.</summary>
    public void SetContent(UIElement content)
    {
        RemoveChild("not_built");
        content.SetBounds(ContentBounds);
        AddChild(content);
        Content = content;
    }

    /// <summary>The window card's content, or null while the window is not built.</summary>
    public UIElement? Content { get; private set; }

    //a drop on the frame or the title goes to the content, as a drop on the content does: the window takes it, not the
    //world under the panel. A drop on the content reaches the content first and does not come here again
    public override void OnDragDrop(DragDropEvent e)
    {
        if (Content is { } content && !content.ScreenBounds.Contains(e.ScreenX, e.ScreenY))
            content.OnDragDrop(e);
    }

    /// <summary>The X button: the host closes the panel.</summary>
    public event Action<HudPanel>? CloseRequested;

    /// <summary>The swap button of a compact panel: the host changes the sides of the compact panels.</summary>
    public event Action? SwapSidesRequested;
}
