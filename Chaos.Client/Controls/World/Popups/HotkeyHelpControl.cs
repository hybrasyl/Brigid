#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Data;
using Chaos.Client.Data.Models;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.Popups;

/// <summary>
///     Hotkey/help overlay using _nhotkem prefab (full-screen 640x480). Background from _nhk_bk.spf, keyboard diagram
///     from _nhk_m.spf at MAIN rect. Hovering a key-group (C00-C13 from _nhotkey prefab) highlights that section on
///     the keyboard and shows a detail explanation image (_nhke##.spf) in the EX area. Key-group rects are in
///     keyboard-local space and offset by MAIN origin. Closed by Escape, Enter, or right-click.
/// </summary>
public sealed class HotkeyHelpControl : PrefabPanel
{
    private const int MAX_KEY_GROUPS = 14;
    private readonly UIImage? DetailDisplay;
    private readonly Texture2D?[] DetailImages = new Texture2D?[MAX_KEY_GROUPS];

    private readonly KeyGroupEntry[] KeyGroups = new KeyGroupEntry[MAX_KEY_GROUPS];
    public HotkeyHelpControl()
        : base("_nhotkem")
    {
        Name = "HotkeyHelp";
        Visible = false;
        UsesControlStack = true;

        //main rect — keyboard diagram position. c## key rects are relative to this.
        var mainRect = GetRect("MAIN");

        //ex area — where detail images appear on hover
        var exRect = GetRect("EX");

        if (exRect != Rectangle.Empty)
        {
            DetailDisplay = new UIImage
            {
                Name = "DetailImage",
                X = exRect.X,
                Y = exRect.Y,
                Width = exRect.Width,
                Height = exRect.Height,
                Visible = false,
                IsHitTestVisible = false
            };

            AddChild(DetailDisplay);
        }

        //load the second control file (_nhotkey) which defines the key-group hit rects
        var keyPrefabSet = DataContext.UserControls.Get("_nhotkey");

        if (keyPrefabSet is null || (mainRect == Rectangle.Empty))
            return;

        for (var i = 0; i < MAX_KEY_GROUPS; i++)
        {
            var name = $"C{i:D2}";

            //standard single-rect key group
            if (keyPrefabSet.Contains(name))
            {
                var (element, texture) = CreateKeyElement(keyPrefabSet[name], mainRect);

                if (element is not null)
                {
                    KeyGroups[i] = new KeyGroupEntry(element, texture, null, null);
                    DetailImages[i] = TryLoadDetailImage(i);
                }

                continue;
            }

            //multi-rect key group (e.g. c02 → c021, c022)
            var subElements = new List<UIImage>();
            var subTextures = new List<Texture2D?>();

            for (var s = 1; s <= 9; s++)
            {
                var subName = $"{name}{s}";

                if (!keyPrefabSet.Contains(subName))
                    continue;

                var (sub, subTex) = CreateKeyElement(keyPrefabSet[subName], mainRect);

                if (sub is not null)
                {
                    subElements.Add(sub);
                    subTextures.Add(subTex);
                }
            }

            if (subElements.Count > 0)
            {
                KeyGroups[i] = new KeyGroupEntry(subElements[0], null, subElements.ToArray(), subTextures.ToArray());
                DetailImages[i] = TryLoadDetailImage(i);
            }
        }
    }

    /// <summary>
    ///     Creates a UIImage element from a _nhotkey prefab control, offset by the MAIN rect origin to convert from
    ///     keyboard-local space to panel space. The element is always visible (so it participates in hit-testing) but
    ///     starts with a null Texture — the highlight texture is returned separately and assigned on hover.
    /// </summary>
    private (UIImage? Image, Texture2D? Texture) CreateKeyElement(ControlPrefab prefab, Rectangle mainRect)
    {
        var rect = prefab.Control.Rect;

        if (rect is null || (prefab.Images.Count == 0))
            return (null, null);

        var r = rect.Value;
        var highlightTexture = UiRenderer.Instance!.GetPrefabTexture("_nhotkey", prefab.Control.Name, 0);

        var image = new UIImage
        {
            Name = prefab.Control.Name,
            X = mainRect.X + (int)r.Left,
            Y = mainRect.Y + (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            Texture = null,
            IsHitTestVisible = false
        };

        AddChild(image);

        return (image, highlightTexture);
    }

    public override void Dispose()
    {
        foreach (var tex in DetailImages)
            tex?.Dispose();

        base.Dispose();
    }

    private int HitTestKeyGroup(int mouseX, int mouseY)
    {
        for (var i = 0; i < MAX_KEY_GROUPS; i++)
        {
            var group = KeyGroups[i];

            if (group.Primary is null)
                continue;

            if (group.SubElements is not null)
            {
                foreach (var sub in group.SubElements)
                    if (sub.ContainsPoint(mouseX, mouseY))
                        return i;

                continue;
            }

            if (group.Primary.ContainsPoint(mouseX, mouseY))
                return i;
        }

        return -1;
    }

    public event CloseHandler? OnClose;

    private void SetKeyGroupHighlighted(int index, bool highlighted)
    {
        var group = KeyGroups[index];

        if (group.SubElements is not null && group.SubTextures is not null)
            for (var i = 0; i < group.SubElements.Length; i++)
                group.SubElements[i].Texture = highlighted ? group.SubTextures[i] : null;
        else
        {
            group.Primary?.Texture = highlighted ? group.PrimaryTexture : null;
        }
    }

    private static Texture2D TryLoadDetailImage(int index) => UiRenderer.Instance!.GetNationalSpfTexture($"_nhke{index:D2}.spf");

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key is Keys.Escape or Keys.F1 or Keys.Enter)
        {
            Hide();
            OnClose?.Invoke();
            e.Handled = true;
        }
    }

    public override void OnClick(ClickEvent e)
    {
        //consume clicks so they don't pass through to the world
        e.Handled = true;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        var newIndex = HitTestKeyGroup(e.ScreenX, e.ScreenY);

        if (newIndex == HoveredIndex)
            return;

        //hide previous key group highlight
        if (HoveredIndex >= 0)
        {
            SetKeyGroupHighlighted(HoveredIndex, false);

            DetailDisplay?.Visible = false;
        }

        HoveredIndex = newIndex;

        //show new key group highlight and detail image
        if (HoveredIndex >= 0)
        {
            SetKeyGroupHighlighted(HoveredIndex, true);

            if ((DetailDisplay is not null) && DetailImages[HoveredIndex] is { } detailTex)
            {
                DetailDisplay.Texture = detailTex;
                DetailDisplay.Visible = true;
            }
        }
    }

    public override void OnMouseLeave()
    {
        if (HoveredIndex >= 0)
        {
            SetKeyGroupHighlighted(HoveredIndex, false);

            DetailDisplay?.Visible = false;
        }

        HoveredIndex = -1;
    }

    private int HoveredIndex = -1;

    private record struct KeyGroupEntry(
        UIImage? Primary,
        Texture2D? PrimaryTexture,
        UIImage[]? SubElements,
        Texture2D?[]? SubTextures);
}