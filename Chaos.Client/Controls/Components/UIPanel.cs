#region
using Chaos.Client.Controls.Generic;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.Components;

public class UIPanel : UIElement
{
    internal bool ChildOrderDirty;

    public Texture2D? Background { get; set; }

    /// <summary>
    ///     When true, this panel captures all input while visible — other controls receive suppressed input (no keys, no mouse
    ///     events) so their animations/timers still tick.
    /// </summary>
    public bool IsModal { get; set; }

    /// <summary>
    ///     When true, this panel participates in the InputDispatcher control stack. PrefabPanel.Show/Hide
    ///     automatically push/remove the panel. Non-PrefabPanel subclasses must push/remove manually.
    /// </summary>
    public bool UsesControlStack { get; set; }

    public List<UIElement> Children { get; } = [];

    public UIPanel()
        => VisibilityChanged += visible =>
        {
            if (!visible)
                ResetInteractionState();
        };

    public void AddChild(UIElement child)
    {
        child.Parent = this;
        Children.Add(child);
        ChildOrderDirty = true;
    }

    public override void Dispose()
    {
        Background?.Dispose();
        Background = null;

        foreach (var child in Children)
            child.Dispose();

        Children.Clear();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        EnsureChildOrder();

        //background fill and border (inlined from base, since we insert Background texture between)
        if (BackgroundColor.HasValue || BorderColor.HasValue)
        {
            var bounds = new Rectangle(ScreenX, ScreenY, Width, Height);

            if (BackgroundColor.HasValue)
                DrawRectClipped(spriteBatch, bounds, BackgroundColor.Value);

            if (BorderColor.HasValue)
            {
                DrawRectClipped(spriteBatch, new Rectangle(bounds.X, bounds.Y, bounds.Width, 1), BorderColor.Value);
                DrawRectClipped(spriteBatch, new Rectangle(bounds.X, bounds.Y + bounds.Height - 1, bounds.Width, 1), BorderColor.Value);
                DrawRectClipped(spriteBatch, new Rectangle(bounds.X, bounds.Y, 1, bounds.Height), BorderColor.Value);
                DrawRectClipped(spriteBatch, new Rectangle(bounds.X + bounds.Width - 1, bounds.Y, 1, bounds.Height), BorderColor.Value);
            }
        }

        if (Background is not null)
            DrawTexture(spriteBatch, Background, new Vector2(ScreenX, ScreenY), Color.White);

        foreach (var child in Children)
            if (child.Visible)
            {
                child.Draw(spriteBatch);
                DebugOverlay.DrawElement(spriteBatch, child);
            }
    }

    internal void EnsureChildOrder()
    {
        if (!ChildOrderDirty)
            return;

        StableSortByZIndex(Children);
        ChildOrderDirty = false;
    }

    public T? FindChild<T>(string name) where T: UIElement
    {
        foreach (var child in Children)
        {
            if (child is T typed && (typed.Name == name))
                return typed;

            if (child is UIPanel panel)
            {
                var found = panel.FindChild<T>(name);

                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    public void RemoveChild(string name)
    {
        for (var i = Children.Count - 1; i >= 0; i--)
            if (Children[i]
                .Name
                .EqualsI(name))
            {
                var child = Children[i];
                Children.RemoveAt(i);
                child.Dispose();
            }
    }

    public override void ResetInteractionState()
    {
        foreach (var child in Children)
            child.ResetInteractionState();
    }

    /// <summary>
    ///     Stable in-place insertion sort by ZIndex. O(n) when already sorted (common case), stable (preserves add-order for
    ///     equal ZIndex), zero allocations.
    /// </summary>
    private static void StableSortByZIndex(List<UIElement> list)
    {
        for (var i = 1; i < list.Count; i++)
        {
            var item = list[i];
            var key = item.ZIndex;
            var j = i - 1;

            while ((j >= 0) && (list[j].ZIndex > key))
            {
                list[j + 1] = list[j];
                j--;
            }

            list[j + 1] = item;
        }
    }

    //panels absorb their own click events by default so popups/dialogs don't leak clicks past their
    //visual bounds to whatever is rendered behind. opt out by overriding without calling base — the
    //Root panel does this so its OnClick can run world-level handlers. drag/move/scroll still bubble
    //normally since terminal handlers like OnRootDragDrop depend on reaching root unhandled.
    public override void OnClick(ClickEvent e) => e.Handled = true;

    public override void OnMouseDown(MouseDownEvent e) => e.Handled = true;

    public override void OnMouseUp(MouseUpEvent e) => e.Handled = true;

    public override void OnDoubleClick(DoubleClickEvent e) => e.Handled = true;

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key != Keys.Tab)
            return;

        //collect all visible istabstop textboxes, sorted by y then x
        List<UITextBox>? tabStops = null;

        foreach (var child in Children)
            if (child is UITextBox { Visible: true, Enabled: true, IsTabStop: true } textBox)
                (tabStops ??= []).Add(textBox);
            else if (child is UIPanel childPanel)
                CollectTabStops(childPanel, ref tabStops);

        if (tabStops is null || (tabStops.Count < 2))
            return;

        tabStops.Sort((a, b) =>
        {
            var cmp = a.ScreenY.CompareTo(b.ScreenY);

            return cmp != 0 ? cmp : a.ScreenX.CompareTo(b.ScreenX);
        });

        //find current and advance
        var currentIndex = -1;

        for (var i = 0; i < tabStops.Count; i++)
            if (tabStops[i].IsFocused)
            {
                currentIndex = i;

                break;
            }

        if (currentIndex < 0)
            return;

        var nextIndex = (currentIndex + (e.Shift ? tabStops.Count - 1 : 1)) % tabStops.Count;
        tabStops[currentIndex].IsFocused = false;
        tabStops[nextIndex].IsFocused = true;
        e.Handled = true;
    }

    private static void CollectTabStops(UIPanel panel, ref List<UITextBox>? tabStops)
    {
        if (!panel.Visible || !panel.Enabled)
            return;

        foreach (var child in panel.Children)
            if (child is UITextBox { Visible: true, Enabled: true, IsTabStop: true } textBox)
                (tabStops ??= []).Add(textBox);
            else if (child is UIPanel childPanel)
                CollectTabStops(childPanel, ref tabStops);
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        EnsureChildOrder();

        var count = Children.Count;

        for (var i = 0; i < count; i++)
        {
            var child = Children[i];

            if (child is { Visible: true, Enabled: true })
                child.Update(gameTime);
        }
    }

}