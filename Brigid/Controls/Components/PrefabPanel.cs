#region
using Brigid.Data;
using Brigid.Data.Models;
using Brigid.Extensions;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Components;

/// <summary>
///     Base class for UI panels built from a ControlPrefabSet. Provides helpers to create buttons, images, text boxes, and
///     layout rects from prefab data. Subclasses selectively create only the controls they need.
/// </summary>
public abstract class PrefabPanel : UIPanel
{
    protected ControlPrefabSet PrefabSet { get; }

    protected PrefabPanel(string prefabName, bool center = true)
    {
        var prefabSet = DataContext.UserControls.Get(prefabName);

        PrefabSet = prefabSet ?? throw new InvalidOperationException($"Failed to load {prefabName} control prefab set");

        //anchor — panel dimensions and background
        var anchor = prefabSet[0];
        var anchorRect = anchor.Control.Rect!.Value;

        Width = (int)anchorRect.Width;
        Height = (int)anchorRect.Height;

        if (center)
        {
            //use anchor position if non-zero, otherwise center on screen
            var anchorLeft = (int)anchorRect.Left;
            var anchorTop = (int)anchorRect.Top;

            if ((anchorLeft != 0) || (anchorTop != 0))
            {
                X = anchorLeft;
                Y = anchorTop;
            } else
                this.CenterOnScreen();
        }

        if (anchor.Images.Count > 0)
            Background = UiRenderer.Instance!.GetPrefabTexture(prefabName, anchor.Control.Name, 0);
    }

    /// <summary>
    ///     Swaps the panel background to the anchor's Nth image frame. Clamped to the available range. No-op if the anchor
    ///     has no images.
    /// </summary>
    protected void SetBackgroundFrame(int frame)
    {
        var anchor = PrefabSet[0];

        if (anchor.Images.Count <= 0)
            return;

        var clamped = Math.Clamp(frame, 0, anchor.Images.Count - 1);
        Background = UiRenderer.Instance!.GetPrefabTexture(PrefabSet.Name, anchor.Control.Name, clamped);
    }

    protected UIButton? CreateButton(string name) => CreateButton<UIButton>(name);

    protected T? CreateButton<T>(string name) where T: UIButton, new()
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<T>(name) is { } existing)
            return existing;

        var button = CreateButtonFromPrefab<T>(PrefabSet[name]);

        if (button is not null)
            AddChild(button);

        return button;
    }

    private T? CreateButtonFromPrefab<T>(ControlPrefab prefab) where T : UIButton, new()
    {
        var rect = prefab.Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        var cache = UiRenderer.Instance!;
        var count = prefab.Images.Count;

        //standard layout (_nbtn, etc): 3 frames per button as [normal, pressed, disabled].
        //exception: _ncarrow (angle/hair arrow buttons) packs all normals together followed by
        //all presseds, so a single button's expanded Images list is [normal, OTHER_BUTTON, pressed]
        //where the middle frame belongs to an adjacent button and must be skipped — for that
        //layout the pressed texture is the LAST entry, not index 1.
        var firstImageName = prefab.Control.Images is { Count: > 0 } ctrlImages ? ctrlImages[0].ImageName : string.Empty;
        var isInterleavedPattern = firstImageName.StartsWithI("_ncarrow");

        var normalTexture = count > 0 ? cache.GetPrefabTexture(PrefabSet.Name, prefab.Control.Name, 0) : null;

        Texture2D? pressedTexture;
        Texture2D? disabledTexture;

        if (isInterleavedPattern)
        {
            pressedTexture = count > 1 ? cache.GetPrefabTexture(PrefabSet.Name, prefab.Control.Name, count - 1) : null;
            disabledTexture = null;
        } else
        {
            pressedTexture = count > 1 ? cache.GetPrefabTexture(PrefabSet.Name, prefab.Control.Name, 1) : null;
            disabledTexture = count > 2 ? cache.GetPrefabTexture(PrefabSet.Name, prefab.Control.Name, 2) : null;
        }

        return new T
        {
            Name = prefab.Control.Name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            NormalTexture = normalTexture,
            PressedTexture = pressedTexture,
            DisabledTexture = disabledTexture,
            SelectedTexture = pressedTexture
        };
    }

    protected UIImage? CreateImage(string name)
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<UIImage>(name) is { } existing)
            return existing;

        var image = CreateImageFromPrefab(PrefabSet[name]);

        if (image is not null)
            AddChild(image);

        return image;
    }

    private UIImage? CreateImageFromPrefab(ControlPrefab prefab)
    {
        var rect = prefab.Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        return new UIImage
        {
            Name = prefab.Control.Name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            Texture = prefab.Images.Count > 0 ? UiRenderer.Instance!.GetPrefabTexture(PrefabSet.Name, prefab.Control.Name, 0) : null
        };
    }

    protected UILabel? CreateLabel(string name, HorizontalAlignment alignment = HorizontalAlignment.Left)
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<UILabel>(name) is { } existing)
        {
            existing.HorizontalAlignment = alignment;

            return existing;
        }

        var rect = PrefabSet[name].Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        var label = new UILabel
        {
            Name = name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height,
            HorizontalAlignment = alignment
        };

        AddChild(label);

        return label;
    }

    protected UIProgressBar? CreateProgressBar(string name)
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<UIProgressBar>(name) is { } existing)
            return existing;

        var prefab = PrefabSet[name];
        var rect = prefab.Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;
        var cache = UiRenderer.Instance!;
        Texture2D[] frames;

        //single-entry image lines in control files encode the last frame index.
        //load all frames (0 through frameindex inclusive) from the referenced spf file.
        if (prefab.Control.Images is [{ FrameIndex: > 1 } _] rawImages)
        {
            (var imageName, var lastFrameIndex) = rawImages[0];
            var frameCount = lastFrameIndex + 1;
            frames = new Texture2D[frameCount];

            for (var i = 0; i < frameCount; i++)
                frames[i] = cache.GetSpfTexture(imageName, i);
        } else
        {
            frames = new Texture2D[prefab.Images.Count];

            for (var i = 0; i < prefab.Images.Count; i++)
                frames[i] = cache.GetPrefabTexture(PrefabSet.Name, name, i);
        }

        var bar = new UIProgressBar(frames)
        {
            Name = name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height
        };

        AddChild(bar);

        return bar;
    }

    protected UITextBox? CreateTextBox(string name, int maxLength = 12)
    {
        if (!PrefabSet.Contains(name))
            return null;

        if (ReuseOrRemoveExistingChild<UITextBox>(name) is { } existing)
        {
            existing.MaxLength = maxLength;

            return existing;
        }

        var textBox = CreateTextBoxFromPrefab(PrefabSet[name]);

        if (textBox is not null)
        {
            textBox.MaxLength = maxLength;
            AddChild(textBox);
        }

        return textBox;
    }

    private UITextBox? CreateTextBoxFromPrefab(ControlPrefab prefab)
    {
        var rect = prefab.Control.Rect;

        if (rect is null)
            return null;

        var r = rect.Value;

        return new UITextBox
        {
            Name = prefab.Control.Name,
            X = (int)r.Left,
            Y = (int)r.Top,
            Width = (int)r.Width,
            Height = (int)r.Height
        };
    }

    protected Rectangle GetRect(string name) => GetRect(PrefabSet, name);

    internal static Rectangle GetRect(ControlPrefabSet prefabSet, string name)
    {
        if (!prefabSet.Contains(name))
            return Rectangle.Empty;

        var rect = prefabSet[name].Control.Rect;

        if (rect is null)
            return Rectangle.Empty;

        var r = rect.Value;

        return new Rectangle(
            (int)r.Left,
            (int)r.Top,
            (int)r.Width,
            (int)r.Height);
    }

    public virtual void Hide()
    {
        if (UsesControlStack)
            InputDispatcher.Instance?.RemoveControl(this);

        Visible = false;
    }

    /// <summary>
    ///     If a child with the given name already exists and is of type T, returns it. Otherwise removes and disposes all
    ///     children with that name, and returns null.
    /// </summary>
    private T? ReuseOrRemoveExistingChild<T>(string name) where T: UIElement
    {
        T? reusable = null;

        for (var i = Children.Count - 1; i >= 0; i--)
        {
            if (Children[i]
                .Name
                .EqualsI(name) is not true)
                continue;

            if (reusable is null && Children[i] is T match)
            {
                reusable = match;

                continue;
            }

            var child = Children[i];
            Children.RemoveAt(i);
            child.Dispose();
        }

        return reusable;
    }

    public virtual void Show()
    {
        if (UsesControlStack)
            InputDispatcher.Instance?.PushControl(this);

        Visible = true;
    }
}