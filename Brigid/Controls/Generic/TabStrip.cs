#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>Which way a <see cref="TabStrip" /> stacks its tabs.</summary>
public enum TabOrientation
{
    /// <summary>Left-to-right along the top of the content area.</summary>
    Horizontal,

    /// <summary>Top-to-bottom down a column beside the content area.</summary>
    Vertical
}

/// <summary>
///     A row or column of <see cref="SelectableTab" />s with exactly one selected. Replaces the tab array +
///     <c>SelectTab</c> loop each modal had hand-rolled.
///     <para>
///         <b>Clicked and selected are deliberately separate.</b> <see cref="TabClicked" /> fires on every click;
///         the selection only moves when someone calls <see cref="SetSelected" />. A modal whose tabs switch
///         locally calls it straight from the handler; one whose content the server owns (the boards modal, where
///         a tab click is a request) leaves the selection alone until the reply lands.
///     </para>
/// </summary>
public sealed class TabStrip : UIPanel
{
    private readonly SelectableTab[] Tabs;

    /// <summary>Raised on every tab click with the tab's index — including a click on the selected tab.</summary>
    public event Action<int>? TabClicked;

    /// <summary>The selected tab index, or -1 when none is.</summary>
    public int SelectedIndex { get; private set; } = -1;

    /// <param name="titles">Tab labels, in order. Also fixes the tab count.</param>
    /// <param name="tabWidth">Width of one tab.</param>
    /// <param name="tabHeight">Height of one tab.</param>
    /// <param name="gap">Space between adjacent tabs.</param>
    /// <param name="orientation">Whether tabs run across or down.</param>
    public TabStrip(
        IReadOnlyList<string> titles,
        int tabWidth,
        int tabHeight,
        int gap = 4,
        TabOrientation orientation = TabOrientation.Horizontal)
    {
        Tabs = new SelectableTab[titles.Count];
        var stride = (orientation == TabOrientation.Horizontal ? tabWidth : tabHeight) + gap;

        for (var i = 0; i < titles.Count; i++)
        {
            var index = i;

            var tab = new SelectableTab(titles[i], tabWidth, tabHeight)
            {
                X = orientation == TabOrientation.Horizontal ? i * stride : 0,
                Y = orientation == TabOrientation.Horizontal ? 0 : i * stride
            };

            tab.Clicked += () => TabClicked?.Invoke(index);
            Tabs[i] = tab;
            AddChild(tab);
        }

        //the strip sizes to its tabs, so a host positions it as one control.
        var span = titles.Count > 0 ? titles.Count * stride - gap : 0;
        Width = orientation == TabOrientation.Horizontal ? span : tabWidth;
        Height = orientation == TabOrientation.Horizontal ? tabHeight : span;
    }

    /// <summary>Moves the selection. Pass -1 to clear it. Out-of-range indices clear it too.</summary>
    public void SetSelected(int index)
    {
        SelectedIndex = (index >= 0) && (index < Tabs.Length) ? index : -1;

        for (var i = 0; i < Tabs.Length; i++)
            Tabs[i].IsSelected = i == SelectedIndex;
    }

}
