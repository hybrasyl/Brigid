#region
using Brigid.Controls.Components;
using Brigid.Data;
using Brigid.Extensions;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups.Options;

/// <summary>
///     Macro editor using _nmacro prefab. Triggered by F3 key or Options → Macro button. Displays 10 editable text fields
///     for macro commands. Loads from player config on open, saves on close.
/// </summary>
public sealed class MacrosListControl : PrefabPanel
{
    private const int MAX_MACROS = 10;
    private const int ROW_HEIGHT = 21;
    private const int LABEL_START_Y = 40;
    private const int TEXTBOX_X = 40;
    private const int TEXTBOX_WIDTH = 385;
    private const int MAX_LENGTH = 63;

    private readonly UITextBox[] MacroTextBoxes = new UITextBox[MAX_MACROS];
    private SlideAnimator Slide;
    private int SlideAnchorY;
    private bool SlideMode;

    public UIButton? CancelButton { get; }
    public UIButton? OkButton { get; }

    public MacrosListControl()
        : base("_nmacro", false)
    {
        Name = "MacroMenu";
        Visible = false;
        UsesControlStack = true;

        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");

        if (OkButton is not null)
            OkButton.Clicked += SaveAndClose;

        if (CancelButton is not null)
            CancelButton.Clicked += Close;

        for (var i = 0; i < MAX_MACROS; i++)
        {
            MacroTextBoxes[i] = new UITextBox
            {
                Name = $"Macro{i}",
                X = TEXTBOX_X,
                Y = LABEL_START_Y + i * ROW_HEIGHT,
                Width = TEXTBOX_WIDTH,
                Height = ROW_HEIGHT,
                MaxLength = MAX_LENGTH,
                ForegroundColor = LegendColors.White,
                IsTabStop = true
            };

            AddChild(MacroTextBoxes[i]);
        }
    }

    private void Close()
    {
        if (SlideMode)
        {
            InputDispatcher.Instance?.RemoveControl(this);
            Slide.SlideOut();
        } else
        {
            Hide();
            OnClose?.Invoke();
        }
    }

    /// <summary>
    ///     Returns the macro value for the given slot index, or empty string if out of range.
    /// </summary>
    public string GetMacroValue(int index) => index is >= 0 and < MAX_MACROS ? MacroTextBoxes[index].Text : string.Empty;

    /// <summary>
    ///     Returns the macro values for all slots.
    /// </summary>
    public string[] GetMacroValues()
    {
        var values = new string[MAX_MACROS];

        for (var i = 0; i < MAX_MACROS; i++)
            values[i] = MacroTextBoxes[i].Text;

        return values;
    }

    public override void Hide()
    {
        InputDispatcher.Instance?.RemoveControl(this);

        if (SlideMode)
            Slide.Hide(this);
        else
            Visible = false;
    }

    public event CloseHandler? OnClose;

    private void Save()
    {
        var macros = GetMacroValues();
        DataContext.LocalPlayerSettings.SaveMacros(macros);
    }

    private void SaveAndClose()
    {
        Save();
        Close();
    }

    /// <summary>
    ///     Populates all macro textboxes from the given values array.
    /// </summary>
    public void SetMacros(string[] macros)
    {
        for (var i = 0; i < MAX_MACROS; i++)
            MacroTextBoxes[i].Text = i < macros.Length ? macros[i] : string.Empty;
    }

    /// <summary>
    ///     Sets the slide anchor. The sub-panel slides left out of the anchor and back into it on close.
    /// </summary>
    public void SetSlideAnchor(int anchorX, int anchorY)
    {
        Slide.SetSlideAnchor(anchorX, Width);
        SlideAnchorY = anchorY;
    }

    /// <summary>
    ///     Shows immediately at top-center of screen (hotkey mode).
    /// </summary>
    public override void Show()
    {
        this.CenterHorizontallyOnScreen();
        Y = 0;
        InputDispatcher.Instance?.PushControl(this);
        Visible = true;
        SlideMode = false;
    }

    /// <summary>
    ///     Slides in from the configured slide anchor (button mode).
    /// </summary>
    public void SlideIn()
    {
        if (Visible)
            return;

        Y = SlideAnchorY;
        InputDispatcher.Instance?.PushControl(this);
        Slide.SlideIn(this);
        SlideMode = true;
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        if (Slide.Update(gameTime, this))
        {
            OnClose?.Invoke();

            return;
        }

        base.Update(gameTime);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (Slide.Sliding)
            return;

        if (e.Key is Keys.Escape or Keys.F3)
        {
            Close();
            e.Handled = true;
        }
    }
}
