#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>
///     Base for the primitive lobby forms (BRIG-116, BRIG-119): a themed, modal <see cref="CenteredModalPanel" /> with
///     label and field rows, a status line above the bottom bar, and a submit button and a Cancel button in place of Close.
///     <para>
///         The screen does the work: <see cref="OnOk" /> asks it to check the fields and send the packet, and
///         <see cref="OnCancel" /> asks it to hide the form. While the screen waits for the server it calls
///         <see cref="SetBusy" />, which disables the submit button and shows the busy text. Validation and server messages
///         show in the status line, not in a popup.
///     </para>
///     <para>
///         Enter submits, except Alt+Enter, which changes the window size. Escape cancels. Tab moves between the text fields.
///         A subclass changes Enter or Escape with <see cref="HandleEnter" /> and <see cref="HandleEscape" />.
///     </para>
/// </summary>
public abstract class LobbyFormPanel : CenteredModalPanel
{
    protected const int LABEL_W = 72;
    protected const int ROW_H = 18;
    protected const int ROW_STRIDE = 24;
    protected const int STATUS_H = 36;
    protected const int ACTION_W = 64;
    protected const int NAME_MAX_LENGTH = 12;
    protected const int PASSWORD_MAX_LENGTH = 8;

    private readonly string BusyText;
    private readonly UILabel Status;
    private readonly List<UITextBox> TextFields = [];

    /// <summary>The button that submits the form.</summary>
    public TextButton SubmitButton { get; }

    /// <summary>The button that cancels the form.</summary>
    public TextButton CancelButton { get; }

    /// <summary>The text of the status line.</summary>
    public string StatusText => Status.Text;

    /// <summary>True while the screen waits for the server to answer. The submit button is disabled.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Raised when the player presses the submit button (or Enter). The screen checks the fields and sends the packet.</summary>
    public event OkHandler? OnOk;

    /// <summary>Raised when the player presses Cancel (or Escape). The screen hides the form.</summary>
    public event CancelHandler? OnCancel;

    /// <param name="name">The panel name, <c>[a-z0-9_]</c>.</param>
    /// <param name="title">The text of the title band.</param>
    /// <param name="submitText">The text of the submit button.</param>
    /// <param name="submitName">The name of the submit button, <c>[a-z0-9_]</c>.</param>
    /// <param name="busyText">The status line while the form is busy.</param>
    /// <param name="width">The panel width.</param>
    /// <param name="height">The panel height.</param>
    protected LobbyFormPanel(
        string name,
        string title,
        string submitText,
        string submitName,
        string busyText,
        int width,
        int height)
        //a form is a child of the lobby root, not of the menu, so it sets the theme itself
        : base(title, width, height, useTheme: true)
    {
        Name = name;
        IsModal = true;
        BusyText = busyText;

        //Cancel takes the place of Close, so that a cancel always reaches the screen
        CloseButton.Visible = false;
        CancelButton = AddBottomBarButton("Cancel", ACTION_W, Cancel, "cancel");
        SubmitButton = AddBottomBarButton(submitText, ACTION_W, Submit, submitName);

        Status = new UILabel
        {
            Name = "status",
            WordWrap = true,
            IsHitTestVisible = false
        };
        Status.SetForegroundRole(ThemeRoles.Text, TextColors.Default);
        AddChild(Status);
    }

    /// <summary>Adds a text field at <paramref name="row" /> of <paramref name="pane" />, right of the label column.</summary>
    protected UITextBox AddField(UIPanel pane, string name, int row, int maxLength, bool masked)
    {
        var box = new UITextBox
        {
            Name = name,
            X = LABEL_W,
            Y = row * ROW_STRIDE,
            Height = ROW_H,
            MaxLength = maxLength,
            IsMasked = masked,
            FocusedBackgroundColor = DialogPalette.RowHoverFill,
            IsTabStop = true
        };

        box.SetForegroundRole(ThemeRoles.InputText, LegendColors.White);
        TextFields.Add(box);
        pane.AddChild(box);

        return box;
    }

    /// <summary>Adds a label at <paramref name="row" /> of <paramref name="parent" />, in the label column.</summary>
    protected void AddFieldLabel(UIPanel parent, string name, string text, int row)
    {
        var label = new UILabel
        {
            Name = name,
            X = 0,
            Y = row * ROW_STRIDE,
            Width = LABEL_W,
            Height = ROW_H,
            VerticalAlignment = VerticalAlignment.Center,
            Text = text,
            IsHitTestVisible = false
        };

        label.SetForegroundRole(ThemeRoles.Text, TextColors.Default);
        parent.AddChild(label);
    }

    /// <summary>The height of a group of <paramref name="rows" /> label and field rows.</summary>
    protected static int RowsHeight(int rows) => rows * ROW_STRIDE - (ROW_STRIDE - ROW_H);

    /// <summary>Makes every text field fill a group of <paramref name="groupWidth" />, right of the label column.</summary>
    protected void SetFieldWidths(int groupWidth)
    {
        foreach (var field in TextFields)
            field.Width = groupWidth - LABEL_W;
    }

    /// <summary>Puts the status line across the bottom of <paramref name="content" />, and returns its top.</summary>
    protected int PlaceStatus(Rectangle content)
    {
        Status.X = content.X;
        Status.Y = content.Bottom - PADDING - STATUS_H;
        Status.Width = content.Width;
        Status.Height = STATUS_H;

        return Status.Y;
    }

    /// <summary>Shows an error in the status line.</summary>
    public void ShowError(string message) => SetStatus(message, true);

    /// <summary>Shows a message that is not an error in the status line.</summary>
    public void ShowStatus(string message) => SetStatus(message, false);

    /// <summary>Removes the message from the status line.</summary>
    public void ClearStatus() => SetStatus(string.Empty, false);

    private void SetStatus(string message, bool isError)
    {
        Status.Text = message;

        if (isError)
            Status.SetForegroundRole(ThemeRoles.TextError, DialogPalette.ErrorText);
        else
            Status.SetForegroundRole(ThemeRoles.Text, TextColors.Default);
    }

    /// <summary>
    ///     True when the player can cancel while the form is busy. False by default: Cancel is disabled and Escape does
    ///     nothing. The server does not see a cancel, so its answer to the request still arrives after the form closes, and the
    ///     screen can then handle that answer as the answer to a different request (BRIG-117). A form opts in only when the
    ///     screen handles that answer.
    /// </summary>
    protected virtual bool CanCancelWhileBusy => false;

    /// <summary>
    ///     Sets the state that waits for the server: the submit button is disabled, and the status line shows the busy text.
    ///     Clearing it enables the submit button and leaves the status line as it is.
    /// </summary>
    public void SetBusy(bool busy)
    {
        IsBusy = busy;
        SubmitButton.SetEnabled(!busy);
        CancelButton.SetEnabled(!busy || CanCancelWhileBusy);

        if (busy)
            ShowStatus(BusyText);
    }

    /// <summary>Asks the screen to submit the form. Does nothing while the form is busy.</summary>
    protected void Submit()
    {
        if (!IsBusy)
            OnOk?.Invoke();
    }

    /// <summary>Asks the screen to cancel the form. Does nothing while the form is busy and cannot cancel.</summary>
    protected void Cancel()
    {
        if (!IsBusy || CanCancelWhileBusy)
            OnCancel?.Invoke();
    }

    /// <summary>Clears <paramref name="field" /> and puts the cursor in it.</summary>
    protected static void ClearAndFocus(UITextBox field)
    {
        field.Text = string.Empty;
        field.IsFocused = true;
    }

    /// <summary>
    ///     Called by <see cref="Show" /> before the form shows, after the fields and the status line are cleared. A subclass
    ///     puts its other choices back to their start values here.
    /// </summary>
    protected virtual void ResetForm() { }

    /// <summary>
    ///     Shows the form with empty fields, an empty status line and the cursor in the first field. Does nothing when the
    ///     form is already visible, so that a second call does not clear it.
    /// </summary>
    public override void Show()
    {
        if (Visible)
            return;

        ClearFields();
        ClearStatus();
        SetBusy(false);
        ResetForm();

        base.Show();

        if (TextFields.Count > 0)
            TextFields[0].IsFocused = true;
    }

    public override void Hide()
    {
        ClearFields();
        base.Hide();
    }

    private void ClearFields()
    {
        foreach (var field in TextFields)
        {
            field.IsFocused = false;
            field.Text = string.Empty;
        }
    }

    /// <summary>Enter. The default submits the form.</summary>
    protected virtual void HandleEnter() => Submit();

    /// <summary>Escape. The default cancels the form.</summary>
    protected virtual void HandleEscape() => Cancel();

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            //Alt+Enter changes the window size; it must not submit the form
            case Keys.Enter when !e.Alt:
                HandleEnter();
                e.Handled = true;

                return;

            case Keys.Escape:
                HandleEscape();
                e.Handled = true;

                return;
        }

        base.OnKeyDown(e);
    }
}
