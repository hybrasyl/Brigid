#region
using Brigid.Controls.Components;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.Generic;

/// <summary>
///     A short message with OK and an optional Cancel: the logout prompt, the connection-lost prompt, the board and
///     exchange results, the group invite, and the lobby messages. A primitive <see cref="CenteredModalPanel" /> that
///     takes the theme: the frame, the buttons and the text colour come from the theme. With no theme, the frame and the
///     buttons have the flat <see cref="DialogPalette" /> look, and the text is white.
///     <para>
///         The panel has a fixed width, and its height fits the wrapped message. OK raises <see cref="OnOk" /> and Cancel
///         raises <see cref="OnCancel" />; the host hides the popup. Enter and Space are OK. Escape is Cancel, or OK when
///         the popup has no Cancel. The popup is modal: it takes every other key, so nothing behind it gets a key while it
///         is open.
///     </para>
/// </summary>
public sealed class OkPopupMessageControl : CenteredModalPanel
{
    private const int PANEL_W = 300;
    private const int ACTION_W = 64;

    //the space above and below the message
    private const int TEXT_GAP = 6;

    //the message area is two to 20 lines tall; the popup clips a longer message
    private const int MIN_LINES = 2;
    private const int MAX_LINES = 20;

    private readonly UILabel MessageLabel;
    private UIElement? PreviousFocus;

    //the FontEngine generation that the panel was last fitted at; a new face or window size wraps the message again
    private int FittedGeneration = -1;

    public TextButton? CancelButton { get; }
    public TextButton OkButton { get; }

    public event CancelHandler? OnCancel;
    public event OkHandler? OnOk;

    /// <param name="title">The text of the title band.</param>
    /// <param name="showCancel">True to show Cancel beside OK.</param>
    public OkPopupMessageControl(string title, bool showCancel = false)
        //FitToText sets the height
        : base(title, PANEL_W, 0, useTheme: true)
    {
        IsModal = true;

        //the popup has no Close: OK and Cancel answer it
        CloseButton.Visible = false;

        if (showCancel)
            CancelButton = AddBottomBarButton("Cancel", ACTION_W, Cancel, "cancel");

        OkButton = AddBottomBarButton("OK", ACTION_W, Ok, "ok");

        MessageLabel = new UILabel
        {
            Name = "message",
            WordWrap = true,
            VerticalAlignment = VerticalAlignment.Top,
            IsHitTestVisible = false
        };
        MessageLabel.SetForegroundRole(ThemeRoles.Text);
        AddChild(MessageLabel);

        FitToText();
    }

    /// <summary>
    ///     Shows the popup with <paramref name="message" />. When the popup is open, the message changes and the popup stays
    ///     open.
    /// </summary>
    public void Show(string message)
    {
        MessageLabel.Text = message;
        FitToText();

        //a second Show while the popup is open (back-to-back server messages) must not record the focus again: the focus
        //is clear at that point, so the real previous focus would be lost
        if (Visible)
            return;

        //record the focus to give it back on Hide, then clear it, so that the keys come to this popup on the control stack
        //and not to the text box that had the focus
        PreviousFocus = InputDispatcher.Instance?.ExplicitFocus;
        InputDispatcher.Instance?.ClearExplicitFocus();

        base.Show();
    }

    public override void Hide()
    {
        base.Hide();
        MessageLabel.Text = string.Empty;

        //give the focus back to the control that had it before the popup (for example the login password box)
        if (PreviousFocus is not null)
        {
            InputDispatcher.Instance?.SetExplicitFocus(PreviousFocus);
            PreviousFocus = null;
        }
    }

    protected override void LayoutContent() => FitToText();

    public override void Update(GameTime gameTime)
    {
        //the label wraps again when the face or the window size changes; the panel then fits the new line count
        if (Visible && (FontEngine.Instance.Generation != FittedGeneration))
            FitToText();

        base.Update(gameTime);
    }

    //sizes the panel to the wrapped message and places the message in the content area
    private void FitToText()
    {
        var content = ContentBounds;

        MessageLabel.X = content.X;
        MessageLabel.Width = content.Width;

        //a new width or a new FontEngine generation wraps the text again
        MessageLabel.Text = MessageLabel.Text;
        FittedGeneration = FontEngine.Instance.Generation;

        var wrappedHeight = MessageLabel.ContentHeight;
        var lines = Math.Clamp(wrappedHeight / TextRenderer.CHAR_HEIGHT, MIN_LINES, MAX_LINES);
        var areaHeight = lines * TextRenderer.CHAR_HEIGHT;
        SetContentHeight(areaHeight + 2 * TEXT_GAP);

        //a message shorter than the area is centered in it
        var textHeight = Math.Min(wrappedHeight, areaHeight);
        MessageLabel.Y = ContentBounds.Y + TEXT_GAP + (areaHeight - textHeight) / 2;
        MessageLabel.Height = textHeight;
    }

    private void Ok() => OnOk?.Invoke();

    private void Cancel() => OnCancel?.Invoke();

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            //Alt+Enter changes the window size; it must not answer the popup
            case Keys.Enter when !e.Alt:
            case Keys.Space:
                Ok();

                break;

            case Keys.Escape:
                if (CancelButton is not null)
                    Cancel();
                else
                    Ok();

                break;
        }

        //the popup is modal: no key goes on to the screen behind it
        e.Handled = true;
    }
}
