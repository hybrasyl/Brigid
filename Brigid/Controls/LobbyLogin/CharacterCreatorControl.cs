#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Data;
using Brigid.Definitions;
using Brigid.Rendering;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>
///     The character creator as a primitive modal (BRIG-116). It replaces the prefab <see cref="CharacterCreationControl" />
///     and keeps its flow: the screen reads the name and the two passwords for the first create packet, and the
///     <c>Selected*</c> values for the second. The appearance rules are in <see cref="CreatorAppearance" />.
///     <para>
///         The species list has only Human, and the create packets do not carry it: the row is a place for BRIG-21's
///         species list. Validation and server messages show in the status line above the buttons, not in a popup. The
///         space below the <c>look</c> group is kept free for BRIG-21's clan and class rows.
///     </para>
///     <para>
///         Enter is Create, and Escape closes an open species list or else is Cancel. Tab moves between the text fields.
///     </para>
/// </summary>
public sealed class CharacterCreatorControl : CenteredModalPanel
{
    private const int PANEL_W = 520;
    private const int PANEL_H = 380;
    private const int ACTION_W = 64;

    private const int PREVIEW_W = 170;
    private const int COLUMN_GAP = 12;
    private const int LABEL_W = 72;
    private const int ROW_H = 18;
    private const int ROW_STRIDE = 24;
    private const int GROUP_GAP = 12;
    private const int STATUS_H = 36;
    private const int STEPPER_W = 120;
    private const int GENDER_TAB_W = 64;
    private const int SWATCH_W = 28;
    private const int SWATCH_H = 16;
    private const int SWATCH_GAP = 4;
    private const int SWATCH_COLUMNS = 7;
    private const int NAME_MAX_LENGTH = 12;
    private const int PASSWORD_MAX_LENGTH = 8;

    private const int ANIM_FRAME_COUNT = 5;
    private const float WALK_FRAME_INTERVAL_MS = 350f;

    private static readonly Color DefaultSwatch = new(128, 128, 128);

    //a swatch name is a literal, so that a ui_panels pack does not depend on the names of the DisplayColor members;
    //the order is CreatorAppearance.HairColors
    private static readonly string[] SwatchNames =
    [
        "teal", "green", "olive", "yellow", "pumpkin", "apple", "violet",
        "default", "navy", "blue", "gray", "carrot", "brown", "black"
    ];

    private readonly AislingRenderer? Renderer;
    private readonly Texture2D?[] FrameTextures = new Texture2D?[ANIM_FRAME_COUNT];

    private readonly PreviewPane Preview;
    private readonly Stepper Facing;
    private readonly InputSlotPane Account;
    private readonly UIPanel Look;
    private readonly TabStrip GenderTabs;
    private readonly Stepper HairStepper;
    private readonly UIPanel HairColorGrid;
    private readonly ColorSwatch[] Swatches;
    private readonly UILabel Status;
    private readonly UITextBox[] TextFields;
    private readonly List<UILabel> FieldLabels = [];

    private bool PreviewDirty = true;
    private bool StatusIsError;
    private int WalkFrame;
    private float WalkTimer;

    public CreatorAppearance Appearance { get; } = new();

    public UITextBox NameField { get; }
    public UITextBox PasswordField { get; }
    public UITextBox PasswordConfirmField { get; }

    /// <summary>The species list. Only Human, and cosmetic: the create packets do not carry a species.</summary>
    public DropDown Species { get; }

    public TextButton CreateButton { get; }
    public TextButton CancelButton { get; }

    public Gender SelectedGender => Appearance.Gender;
    public byte SelectedHairStyle => Appearance.HairStyle;
    public DisplayColor SelectedHairColor => Appearance.HairColor;

    /// <summary>The text of the status line.</summary>
    public string StatusText => Status.Text;

    /// <summary>True while the screen waits for the server to answer a creation. Create is disabled.</summary>
    public bool IsBusy { get; private set; }

    /// <summary>Raised when the player presses Create (or Enter). The screen checks the fields and sends the packets.</summary>
    public event OkHandler? OnOk;

    /// <summary>Raised when the player presses Cancel (or Escape). The screen hides the creator.</summary>
    public event CancelHandler? OnCancel;

    /// <param name="renderer">Draws the walking preview. With null, the preview is empty.</param>
    /// <param name="swatchColor">Gives the colour of a hair colour swatch, or null for a grey swatch.</param>
    public CharacterCreatorControl(AislingRenderer? renderer = null, Func<DisplayColor, Color?>? swatchColor = null)
        : base("Create a Character", PANEL_W, PANEL_H)
    {
        Name = "charcreate";
        IsModal = true;
        Renderer = renderer;

        //the creator is a child of the lobby root, not of the menu, so it sets the theme itself
        UseTheme = true;

        //Cancel takes the place of Close, so that a cancel always reaches the screen
        CloseButton.Visible = false;
        CancelButton = AddBottomBarButton("Cancel", ACTION_W, () => OnCancel?.Invoke(), "cancel");
        CreateButton = AddBottomBarButton("Create", ACTION_W, Submit, "create");

        Preview = new PreviewPane { Name = "preview" };
        Facing = new Stepper(STEPPER_W, ROW_H) { Name = "facing" };
        Facing.SetValue("Facing");

        //the legacy creator's left arrow turned the preview clockwise
        Facing.Previous += Appearance.TurnClockwise;
        Facing.Next += Appearance.TurnCounterClockwise;
        Preview.AddChild(Facing);
        AddChild(Preview);

        Account = new InputSlotPane { Name = "account" };
        AddFieldLabel(Account, "name_label", "Name", 0);
        NameField = AddField("name", 0, NAME_MAX_LENGTH, false);
        AddFieldLabel(Account, "password_label", "Password", 1);
        PasswordField = AddField("password", 1, PASSWORD_MAX_LENGTH, true);
        AddFieldLabel(Account, "confirm_label", "Confirm", 2);
        PasswordConfirmField = AddField("confirm", 2, PASSWORD_MAX_LENGTH, true);
        TextFields = [NameField, PasswordField, PasswordConfirmField];
        AddChild(Account);

        Look = new UIPanel { Name = "look" };

        //the open list covers the gender row, so the list draws above it
        Species = new DropDown(["Human"], STEPPER_W, ROW_H) { Name = "species", ZIndex = 1 };
        AddFieldLabel(Look, "species_label", "Species", 0);
        AddLookControl(Species, 0);

        GenderTabs = new TabStrip(["Male", "Female"], GENDER_TAB_W, ROW_H) { Name = "gender" };
        GenderTabs.TabClicked += index => Appearance.SetGender(index == 0 ? Gender.Male : Gender.Female);
        AddFieldLabel(Look, "gender_label", "Gender", 1);
        AddLookControl(GenderTabs, 1);

        HairStepper = new Stepper(STEPPER_W, ROW_H) { Name = "hair" };
        HairStepper.Previous += Appearance.PreviousHairStyle;
        HairStepper.Next += Appearance.NextHairStyle;
        AddFieldLabel(Look, "hair_label", "Hair", 2);
        AddLookControl(HairStepper, 2);

        HairColorGrid = new UIPanel { Name = "haircolor" };
        Swatches = new ColorSwatch[CreatorAppearance.HairColors.Count];

        for (var i = 0; i < Swatches.Length; i++)
        {
            var color = CreatorAppearance.HairColors[i];

            var swatch = new ColorSwatch(swatchColor?.Invoke(color) ?? DefaultSwatch)
            {
                Name = SwatchNames[i],
                X = i % SWATCH_COLUMNS * (SWATCH_W + SWATCH_GAP),
                Y = i / SWATCH_COLUMNS * (SWATCH_H + SWATCH_GAP),
                Width = SWATCH_W,
                Height = SWATCH_H
            };

            swatch.Clicked += () => Appearance.SetHairColor(color);
            Swatches[i] = swatch;
            HairColorGrid.AddChild(swatch);
        }

        var rows = (Swatches.Length + SWATCH_COLUMNS - 1) / SWATCH_COLUMNS;
        HairColorGrid.Width = SWATCH_COLUMNS * (SWATCH_W + SWATCH_GAP) - SWATCH_GAP;
        HairColorGrid.Height = rows * (SWATCH_H + SWATCH_GAP) - SWATCH_GAP;
        AddFieldLabel(Look, "haircolor_label", "Colour", 3);
        AddLookControl(HairColorGrid, 3);
        AddChild(Look);

        Status = new UILabel
        {
            Name = "status",
            WordWrap = true,
            IsHitTestVisible = false
        };
        AddChild(Status);

        Appearance.Changed += OnAppearanceChanged;
        LayoutContent();
        SyncSelection();
    }

    /// <summary>
    ///     The colour of a hair swatch: the middle shade of its dye ramp in <c>color0.tbl</c>, or null when the table does not
    ///     have the colour. Pass it to the constructor in the client; tests pass nothing and get grey swatches.
    /// </summary>
    public static Color? DyeSwatchColor(DisplayColor color)
    {
        var table = DataContext.AislingDrawData.DyeColorTable;

        if (!table.Contains((byte)color))
            return null;

        var shades = table[(byte)color].Colors;

        if (shades.Length == 0)
            return null;

        var shade = shades[shades.Length / 2];

        return new Color(shade.Red, shade.Green, shade.Blue);
    }

    //the preview fills the left column and the form the right; a themed frame makes the content area smaller
    protected override void LayoutContent()
    {
        var content = ContentBounds;

        Status.X = content.X;
        Status.Y = content.Bottom - PADDING - STATUS_H;
        Status.Width = content.Width;
        Status.Height = STATUS_H;

        Preview.X = content.X;
        Preview.Y = content.Y + PADDING;
        Preview.Width = PREVIEW_W;
        Preview.Height = Status.Y - PADDING - Preview.Y;

        Facing.X = (Preview.Width - Facing.Width) / 2;
        Facing.Y = Preview.Height - PADDING - Facing.Height;

        var formX = Preview.X + PREVIEW_W + COLUMN_GAP;
        var formWidth = content.Right - formX;

        Account.X = formX;
        Account.Y = Preview.Y;
        Account.Width = formWidth;
        Account.Height = 3 * ROW_STRIDE - (ROW_STRIDE - ROW_H);

        foreach (var field in TextFields)
            field.Width = formWidth - LABEL_W;

        Look.X = formX;
        Look.Y = Account.Y + Account.Height + GROUP_GAP;
        Look.Width = formWidth;
        Look.Height = 3 * ROW_STRIDE + HairColorGrid.Height;
    }

    private UITextBox AddField(string name, int row, int maxLength, bool masked)
    {
        var box = new UITextBox
        {
            Name = name,
            X = LABEL_W,
            Y = row * ROW_STRIDE,
            Height = ROW_H,
            MaxLength = maxLength,
            IsMasked = masked,
            ForegroundColor = LegendColors.White,
            FocusedBackgroundColor = DialogPalette.RowHoverFill,
            IsTabStop = true
        };

        Account.AddChild(box);

        return box;
    }

    private void AddLookControl(UIElement control, int row)
    {
        control.X = LABEL_W;
        control.Y = row * ROW_STRIDE;
        Look.AddChild(control);
    }

    private void AddFieldLabel(UIPanel parent, string name, string text, int row)
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

        FieldLabels.Add(label);
        parent.AddChild(label);
    }

    private void OnAppearanceChanged()
    {
        PreviewDirty = true;
        SyncSelection();
    }

    //the form shows every selection at all times
    private void SyncSelection()
    {
        GenderTabs.SetSelected(Appearance.Gender == Gender.Male ? 0 : 1);
        HairStepper.SetValue($"Style {Appearance.HairStyle}/{Appearance.MaxHairStyle}");

        for (var i = 0; i < Swatches.Length; i++)
            Swatches[i].IsSelected = CreatorAppearance.HairColors[i] == Appearance.HairColor;
    }

    private void Submit()
    {
        if (!IsBusy)
            OnOk?.Invoke();
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
        StatusIsError = isError;
    }

    /// <summary>
    ///     Sets the state that waits for the server: Create is disabled, and the status line says that the character is being
    ///     created. Clearing it enables Create and leaves the status line as it is.
    /// </summary>
    public void SetBusy(bool busy)
    {
        IsBusy = busy;
        CreateButton.SetEnabled(!busy);

        if (busy)
            ShowStatus("Creating the character...");
    }

    /// <summary>Clears the two password fields and puts the cursor in the first.</summary>
    public void ClearPasswords()
    {
        PasswordField.Text = string.Empty;
        PasswordConfirmField.Text = string.Empty;
        PasswordField.IsFocused = true;
    }

    /// <summary>Clears the name field and puts the cursor in it.</summary>
    public void ClearName()
    {
        NameField.Text = string.Empty;
        NameField.IsFocused = true;
    }

    /// <summary>
    ///     Shows the creator with empty fields, the starting appearance and the cursor in the name field. Does nothing when the
    ///     creator is already visible, so that a second call does not clear the form.
    /// </summary>
    public override void Show()
    {
        if (Visible)
            return;

        foreach (var field in TextFields)
        {
            field.Text = string.Empty;
            field.IsFocused = false;
        }

        Species.SetSelected(0);
        ClearStatus();
        SetBusy(false);
        Appearance.Reset();
        WalkFrame = 0;
        WalkTimer = 0;

        base.Show();
        NameField.IsFocused = true;
    }

    public override void Hide()
    {
        foreach (var field in TextFields)
        {
            field.IsFocused = false;
            field.Text = string.Empty;
        }

        base.Hide();
    }

    public override void Update(GameTime gameTime)
    {
        if (Visible)
        {
            if (PreviewDirty)
            {
                RenderWalkFrames();
                PreviewDirty = false;
            }

            WalkTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

            if (WalkTimer >= WALK_FRAME_INTERVAL_MS)
            {
                WalkTimer -= WALK_FRAME_INTERVAL_MS;
                WalkFrame = (WalkFrame + 1) % ANIM_FRAME_COUNT;
            }

            Preview.Frame = FrameTextures[WalkFrame];
        }

        base.Update(gameTime);
    }

    private void RenderWalkFrames()
    {
        if (Renderer is null)
            return;

        for (var i = 0; i < ANIM_FRAME_COUNT; i++)
        {
            FrameTextures[i]?.Dispose();
            FrameTextures[i] = Renderer.RenderPreview(
                Appearance.Gender,
                Appearance.HairStyle,
                Appearance.HairColor,
                Appearance.Direction,
                i);
        }
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        var text = ThemeColor(ThemeRoles.Text, TextColors.Default);

        foreach (var label in FieldLabels)
            label.ForegroundColor = text;

        Status.ForegroundColor = StatusIsError ? ThemeColor(ThemeRoles.TextError, DialogPalette.ErrorText) : text;

        base.Draw(spriteBatch);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            //Alt+Enter changes the window size; it must not submit the form
            case Keys.Enter when !e.Alt:
                Submit();
                e.Handled = true;

                return;

            case Keys.Escape:
                if (Species.IsOpen)
                    Species.Close();
                else
                    OnCancel?.Invoke();

                e.Handled = true;

                return;
        }

        base.OnKeyDown(e);
    }

    public override void Dispose()
    {
        Appearance.Changed -= OnAppearanceChanged;
        Preview.Frame = null;

        for (var i = 0; i < FrameTextures.Length; i++)
        {
            FrameTextures[i]?.Dispose();
            FrameTextures[i] = null;
        }

        base.Dispose();
    }

    /// <summary>The preview box: a dark slot with the walking figure in its centre. The renderer does not belong to it.</summary>
    private sealed class PreviewPane : UIPanel
    {
        public Texture2D? Frame { get; set; }

        protected override void DrawChrome(SpriteBatch spriteBatch, Rectangle bounds)
        {
            DrawRectClipped(spriteBatch, bounds, DialogPalette.SlotFill);
            DrawBorderClipped(spriteBatch, bounds, ThemeColor(ThemeRoles.Divider, DialogPalette.Divider));

            if (Frame is null)
                return;

            //the composite texture has padding on each side, so the body centre is at CANVAS_CENTER_X, not at its middle
            var x = bounds.X + bounds.Width / 2 - AislingRenderer.CANVAS_CENTER_X;
            var y = bounds.Y + bounds.Height / 2 - AislingRenderer.BODY_CENTER_Y;
            DrawTexture(spriteBatch, Frame, new Vector2(x, y), Color.White);
        }
    }
}
