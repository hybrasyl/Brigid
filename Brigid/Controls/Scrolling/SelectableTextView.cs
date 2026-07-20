#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.Scrolling;

/// <summary>
///     A scrollable block of read-only but selectable wrapped text: a <see cref="ScrollView" /> owning a
///     word-wrapped <see cref="UILabel" /> bound through a <see cref="LabelScrollSource" />. The shape every
///     from-scratch "body text" surface needs (a board post, an ability description), packaged so a consumer can't
///     forget the <c>Sync</c>/<c>ScrollToStart</c> pair on a fresh bind or the key forwarding a selectable label
///     needs when it sits under a modal that swallows keys.
/// </summary>
public sealed class SelectableTextView : UIPanel
{
    private readonly ScrollView Scroll;
    private readonly UILabel Label;

    public SelectableTextView(Rectangle bounds)
    {
        X = bounds.X;
        Y = bounds.Y;
        Width = bounds.Width;
        Height = bounds.Height;

        Scroll = new ScrollView
        {
            X = 0,
            Y = 0,
            Width = bounds.Width,
            Height = bounds.Height
        };

        Label = new UILabel
        {
            X = 0,
            Y = 0,
            Width = Scroll.ContentWidth,
            Height = Scroll.ContentHeight,
            PaddingLeft = 0,
            PaddingRight = 2,
            PaddingTop = 0,
            WordWrap = true,
            ForegroundColor = TextColors.Default,
            IsSelectable = true
        };

        Scroll.AddChild(Label);
        Scroll.SetSource(new LabelScrollSource(Label));
        AddChild(Scroll);
    }

    /// <summary>Replaces the text, re-derives the scroll metrics, and returns to the top.</summary>
    public void SetText(string text, Color? color = null)
    {
        Label.Text = text;

        if (color is { } value)
            Label.ForegroundColor = value;

        Scroll.Sync();
        Scroll.ScrollToStart();
    }

    /// <summary>
    ///     Routes a key to the label so Ctrl+C / select-all / caret navigation work. A host modal is the top
    ///     control, so keyboard events stop there instead of descending — it forwards them here.
    /// </summary>
    public void ForwardKey(KeyDownEvent e) => Label.OnKeyDown(e);
}
