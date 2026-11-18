#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Controls.World.Hud.Main;
using Brigid.Rendering;
#endregion

namespace Brigid.Controls.World.Popups.Profile;

/// <summary>
///     The editor for the player's profile text (BRIG-122). A primitive <see cref="CenteredModalPanel" /> that takes the
///     theme: a multiline text box on an <see cref="InputSlotPane" />, and Save and Cancel on the bottom bar.
///     <para>
///         The text box has the size of the profile text box of the Details window of the new HUD (BRIG-151), and it
///         refuses text that does not fit in it, so the editor takes exactly the text that the Details window shows. The
///         text that fits also depends on the font face.
///         Save hides the editor and raises <see cref="OnSave" />. Cancel and Escape hide it with no change. Enter starts a
///         new line.
///     </para>
/// </summary>
public sealed class SelfProfileTextEditorControl : CenteredModalPanel
{
    private const int ACTION_W = 64;

    //the text box of the Details window; 256 characters at most, as before
    private const int TEXT_W = MainHudLayout.DETAILS_PROFILE_WIDTH;
    private const int TEXT_H = MainHudLayout.DETAILS_PROFILE_HEIGHT;
    private const int PANEL_W = TEXT_W + 2 * PADDING + 2;
    private const int MAX_LENGTH = 256;

    private readonly InputSlotPane EditorPane;
    private readonly UITextBox TextBox;

    public event ProfileTextSavedHandler? OnSave;

    public SelfProfileTextEditorControl()
        //SetContentHeight sets the height
        : base("Profile", PANEL_W, 0, useTheme: true)
    {
        Name = "profiletext";
        IsModal = true;

        //Cancel takes the place of Close; Save sits left of it
        CloseButton.Visible = false;
        AddBottomBarButton("Cancel", ACTION_W, Hide, "cancel");
        AddBottomBarButton("Save", ACTION_W, Save, "save");

        EditorPane = new InputSlotPane { Name = "editor" };

        TextBox = new UITextBox
        {
            Name = "text",
            X = 0,
            Y = 0,
            Width = TEXT_W,
            Height = TEXT_H,
            IsMultiLine = true,
            ClampToVisibleArea = true,
            MaxLength = MAX_LENGTH,
            FocusedBackgroundColor = DialogPalette.InputFocus
        };
        TextBox.SetForegroundRole(ThemeRoles.InputText);
        EditorPane.AddChild(TextBox);
        AddChild(EditorPane);

        SetContentHeight(TEXT_H + 2 * PADDING);
        LayoutContent();
    }

    //the text box is centered in the content area
    protected override void LayoutContent()
    {
        var content = ContentBounds;

        EditorPane.X = content.X + (content.Width - TEXT_W) / 2;
        EditorPane.Y = content.Y + PADDING;
        EditorPane.Width = TEXT_W;
        EditorPane.Height = TEXT_H;
    }

    /// <summary>Shows the editor with <paramref name="text" />, the cursor at the start.</summary>
    public void Show(string text)
    {
        TextBox.Text = text;
        TextBox.CursorPosition = 0;
        TextBox.ScrollOffset = 0;

        base.Show();
        TextBox.IsFocused = true;
    }

    public override void Hide()
    {
        base.Hide();
        TextBox.IsFocused = false;
    }

    private void Save()
    {
        var text = TextBox.Text;

        Hide();
        OnSave?.Invoke(text);
    }
}
