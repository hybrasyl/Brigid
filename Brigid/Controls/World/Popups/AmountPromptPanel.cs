#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     Base for the amount prompts (BRIG-122): a themed <see cref="CenteredModalPanel" /> with a line of text, one number
///     field on an <see cref="InputSlotPane" />, and OK and Cancel on the bottom bar. <see cref="ItemAmountControl" /> and
///     <see cref="GoldAmountControl" /> derive from it.
///     <para>
///         The field takes digits only. OK is disabled, and Enter does nothing, while the field is empty. OK and Enter hide
///         the prompt and raise <see cref="OnConfirm" /> with the amount, when the amount is more than 0; an amount larger
///         than <see cref="uint.MaxValue" /> becomes <see cref="uint.MaxValue" />. Cancel and Escape hide it. Each close
///         raises the base <see cref="CenteredModalPanel.OnClose" /> once. A caller centres the prompt with
///         <see cref="CenteredModalPanel.SetViewportBounds" /> before it shows it.
///     </para>
/// </summary>
public abstract class AmountPromptPanel : CenteredModalPanel
{
    private const int PANEL_W = 220;
    private const int ACTION_W = 64;
    private const int GAP = 6;
    private const int PROMPT_H = 14;
    private const int FIELD_H = 18;
    private const int CONTENT_H = GAP + PROMPT_H + GAP + FIELD_H + GAP;

    private readonly UILabel Prompt;
    private readonly InputSlotPane FieldPane;

    /// <summary>The number field.</summary>
    public UITextBox AmountTextBox { get; }

    public TextButton OkButton { get; }
    public TextButton CancelButton { get; }

    /// <summary>Raised when the player confirms an amount more than 0. The parameter is the amount.</summary>
    public event AmountConfirmedHandler? OnConfirm;

    /// <param name="name">The panel name, <c>[a-z0-9_]</c>.</param>
    /// <param name="title">The text of the title band.</param>
    /// <param name="prompt">The line of text above the field.</param>
    /// <param name="maxLength">The most digits that the field takes.</param>
    protected AmountPromptPanel(string name, string title, string prompt, int maxLength)
        //SetContentHeight sets the height
        : base(title, PANEL_W, 0, useTheme: true)
    {
        Name = name;
        IsModal = true;

        //Cancel takes the place of Close; OK sits left of it
        CloseButton.Visible = false;
        CancelButton = AddBottomBarButton("Cancel", ACTION_W, Hide, "cancel");
        OkButton = AddBottomBarButton("OK", ACTION_W, Confirm, "ok");
        OkButton.SetEnabled(false);

        Prompt = new UILabel
        {
            Name = "prompt",
            Text = prompt,
            Height = PROMPT_H,
            VerticalAlignment = VerticalAlignment.Center,
            IsHitTestVisible = false
        };
        Prompt.SetForegroundRole(ThemeRoles.Text, LegendColors.White);
        AddChild(Prompt);

        FieldPane = new InputSlotPane { Name = "field" };

        AmountTextBox = new UITextBox
        {
            Name = "amount",
            X = 0,
            Y = 0,
            Height = FIELD_H,
            MaxLength = maxLength,
            DigitsOnly = true,
            FocusedBackgroundColor = DialogPalette.RowHoverFill
        };
        AmountTextBox.SetForegroundRole(ThemeRoles.InputText, LegendColors.White);
        FieldPane.AddChild(AmountTextBox);
        AddChild(FieldPane);

        SetContentHeight(CONTENT_H);
        LayoutContent();
    }

    //the prompt above the field, both the width of the content area
    protected override void LayoutContent()
    {
        var content = ContentBounds;

        Prompt.X = content.X;
        Prompt.Y = content.Y + GAP;
        Prompt.Width = content.Width;

        FieldPane.X = content.X;
        FieldPane.Y = Prompt.Y + PROMPT_H + GAP;
        FieldPane.Width = content.Width;
        FieldPane.Height = FIELD_H;
        AmountTextBox.Width = content.Width;
    }

    /// <summary>Shows the prompt with an empty field and the cursor in it.</summary>
    public override void Show()
    {
        if (Visible)
            return;

        AmountTextBox.Text = string.Empty;
        OkButton.SetEnabled(false);

        base.Show();
        AmountTextBox.IsFocused = true;
    }

    public override void Hide()
    {
        if (!Visible)
            return;

        base.Hide();
        AmountTextBox.IsFocused = false;
    }

    public override void Update(GameTime gameTime)
    {
        if (Visible)
        {
            var hasText = AmountTextBox.Text.Length > 0;

            if (OkButton.Enabled != hasText)
                OkButton.SetEnabled(hasText);
        }

        base.Update(gameTime);
    }

    private void Confirm()
    {
        var text = AmountTextBox.Text;

        //an empty field has nothing to confirm, as OK is disabled
        if (text.Length == 0)
            return;

        Hide();

        //the field takes digits only, so the parse fails only when the number is too large for a ulong
        var amount = ulong.TryParse(text, out var parsed) ? (uint)Math.Min(parsed, uint.MaxValue) : uint.MaxValue;

        if (amount > 0)
            OnConfirm?.Invoke(amount);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        //Alt+Enter changes the window size; it must not confirm the prompt
        if ((e.Key == Keys.Enter) && !e.Alt)
        {
            Confirm();
            e.Handled = true;

            return;
        }

        //Escape hides the prompt, and the base takes every other key
        base.OnKeyDown(e);
    }
}
