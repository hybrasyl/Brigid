#region
using Brigid.Controls.Components;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A primitive, asset-free selectable tab: a centered label in a framed box that shows a highlighted fill, border and
///     text when <see cref="IsSelected" />. <see cref="TabStrip" /> uses it, and so do the class filters of
///     <c>WorldListControl</c> and the categories of <c>BankShopPanel</c>, so tab columns look the same on every
///     from-scratch panel. It has no hover look. The <see cref="Hovered" /> and <see cref="Unhovered" /> events let a
///     host show a tooltip, for example the bank and shop category name.
///     <para>
///         A themed tab draws the <see cref="ThemeRoles.TabNormal" /> or <see cref="ThemeRoles.TabSelected" /> part, its
///         text in <see cref="ThemeRoles.Text" /> or <see cref="ThemeRoles.TextSelected" />. With no part, it draws the flat
///         <see cref="DialogPalette" /> box, its idle border in <see cref="ThemeRoles.Border" /> and its selected border in
///         <see cref="ThemeRoles.TextSelected" />.
///     </para>
/// </summary>
public sealed class SelectableTab : UIPanel
{
    //2px horizontal inset keeps a long label (set through SetText) off the 1px border
    private const int LABEL_INSET = 2;

    private readonly UILabel Label;

    public event ClickedHandler? Clicked;

    /// <summary>
    ///     Raised on a right click at a screen point, so that a host can open a menu for the tab there. With no handler, a
    ///     right click does nothing.
    /// </summary>
    public event Action<int, int>? RightClicked;
    public event Action? Hovered;
    public event Action? Unhovered;

    /// <summary>
    ///     True when the tab has news that its host wants the player to see, for example a chat tab with a message that
    ///     came while another tab was selected: the label of a tab that is not selected shows in
    ///     <see cref="ThemeRoles.TextTitle" />.
    /// </summary>
    public bool IsMarked
    {
        get;
        set
        {
            field = value;
            RefreshVisual();
        }
    }

    public bool IsSelected
    {
        get;
        set
        {
            field = value;
            RefreshVisual();
        }
    }

    public SelectableTab(string text, int width, int height)
    {
        Width = width;
        Height = height;
        BackgroundColor = DialogPalette.TabIdleFill;
        BorderColor = DialogPalette.FrameBorder;

        Label = new UILabel
        {
            X = LABEL_INSET,
            Y = 0,
            Width = width - 2 * LABEL_INSET,
            Height = height,
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            ShrinkToFit = false,
            IsHitTestVisible = false
        };

        AddChild(Label);
        RefreshVisual();
    }

    public void SetText(string text) => Label.Text = text;

    /// <summary>The colour that the label draws, for the tests.</summary>
    internal Color LabelColorForTests
    {
        get
        {
            Label.ApplyForegroundRole();

            return Label.DrawnForegroundColor;
        }
    }

    //the label fills the tab, also when a host sizes the tab to its text after it is made
    public override void SetBounds(Rectangle bounds)
    {
        base.SetBounds(bounds);

        Label.Width = bounds.Width - 2 * LABEL_INSET;
        Label.Height = bounds.Height;
    }

    //the flat border is part of the look: the outline when idle, the selection accent when selected
    protected internal override Color? DrawnBorderColor
        => BorderColor is null ? null
            : IsSelected ? ThemeColor(ThemeRoles.TextSelected)
            : ThemeColor(ThemeRoles.Border);

    //a selected tab with no tab.selected part uses tab.normal; with no theme the tab keeps its flat look
    protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
    {
        var role = IsSelected ? ThemeRoles.TabSelected : ThemeRoles.TabNormal;

        if (TryDrawThemePart(spriteBatch, role, bounds, ThemeRoles.TabNormal))
            return;

        base.DrawChrome(spriteBatch, bounds);
    }

    private void RefreshVisual()
    {
        BackgroundColor = IsSelected ? DialogPalette.TabSelectedFill : DialogPalette.TabIdleFill;
        //the label takes the theme colour at draw time, because the tab takes the theme from its parent. A mark goes
        //before the selection: a selected tab has a mark only while its content is hidden (a collapsed chat box)
        if (IsMarked)
            Label.SetForegroundRole(ThemeRoles.TextTitle);
        else if (IsSelected)
            Label.SetForegroundRole(ThemeRoles.TextSelected);
        else
            Label.SetForegroundRole(ThemeRoles.Text);
    }

    public override void OnMouseEnter() => Hovered?.Invoke();

    public override void OnMouseLeave() => Unhovered?.Invoke();

    public override void OnClick(ClickEvent e)
    {
        if ((e.Button == MouseButton.Right) && RightClicked is { } rightClicked)
        {
            rightClicked(e.ScreenX, e.ScreenY);
            e.Handled = true;

            return;
        }

        if (e.Button != MouseButton.Left)
            return;

        Clicked?.Invoke();
        e.Handled = true;
    }
}
