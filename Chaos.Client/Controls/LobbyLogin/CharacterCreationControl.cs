#region
using Chaos.Client.Controls.Components;
using Chaos.DarkAges.Definitions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.LobbyLogin;

public sealed class CharacterCreationControl : PrefabPanel
{
    private const int MAX_MALE_HAIR_STYLE = 18;
    private const int MAX_FEMALE_HAIR_STYLE = 17;
    private const int HAIR_COLOR_COLUMNS = 7;
    private const int HAIR_COLOR_ROWS = 2;

    private static readonly DisplayColor[] SWATCH_COLOR_MAP =
    [
        DisplayColor.Teal,
        DisplayColor.Green,
        DisplayColor.Olive,
        DisplayColor.Yellow,
        DisplayColor.Pumpkin,
        DisplayColor.Apple,
        DisplayColor.Violet,
        DisplayColor.Default,
        DisplayColor.Navy,
        DisplayColor.Blue,
        DisplayColor.Gray,
        DisplayColor.Carrot,
        DisplayColor.Brown,
        DisplayColor.Black
    ];

    private const int ANIM_FRAME_COUNT = 5;
    private const float WALK_FRAME_INTERVAL_MS = 350f;

    private readonly Texture2D?[] AnimFrameTextures = new Texture2D?[ANIM_FRAME_COUNT];
    private readonly Texture2D? FemaleSelected;
    private readonly UIElement? FemaleToggleArea;
    private readonly Texture2D? FemaleUnselected;
    private readonly Texture2D? MaleSelected;
    private readonly UIElement? MaleToggleArea;

    //gender toggle — custom hover behavior, not standard buttons
    private readonly Texture2D? MaleUnselected;
    private readonly int PreviewHeight;
    private readonly int PreviewWidth;

    //preview area
    private readonly int PreviewX;
    private readonly int PreviewY;
    private readonly AislingRenderer Renderer;

    //hair color swatch
    private readonly UIImage? SwatchImage;
    private bool FemaleHovered;

    private bool MaleHovered;
    private bool PreviewDirty = true;
    private int WalkFrame;
    private float WalkTimer;

    public int SelectedDirection { get; private set; } = 1;
    public Gender SelectedGender { get; private set; } = Gender.Male;
    public DisplayColor SelectedHairColor { get; private set; } = DisplayColor.Default;
    public byte SelectedHairStyle { get; private set; } = 1;
    public UIButton? AngleLeftButton { get; }
    public UIButton? AngleRightButton { get; }
    public UIButton? CancelButton { get; }
    public UIButton? HairLeftButton { get; }
    public UIButton? HairRightButton { get; }

    //text fields — type 7 with 0 images, manually created
    public UITextBox? NameField { get; }

    //buttons
    public UIButton? OkButton { get; }
    public UITextBox? PasswordConfirmField { get; }
    public UITextBox? PasswordField { get; }

    public CharacterCreationControl(AislingRenderer renderer)
        : base("_ncreate", false)
    {
        Renderer = renderer;
        BackgroundColor = Color.Black;
        Name = "CharacterCreation";
        Visible = false;
        UsesControlStack = true;
        X = 0;
        Y = 0;

        //buttons
        OkButton = CreateButton("OK");
        CancelButton = CreateButton("Cancel");
        AngleLeftButton = CreateButton("AngleLeft");
        AngleRightButton = CreateButton("AngleRight");
        HairLeftButton = CreateButton("HairLeft");
        HairRightButton = CreateButton("HairRight");

        if (OkButton is not null)
            OkButton.Clicked += () => OnOk?.Invoke();

        if (CancelButton is not null)
            CancelButton.Clicked += () => OnCancel?.Invoke();

        if (AngleLeftButton is not null)
            AngleLeftButton.Clicked += OnAngleLeftClicked;

        if (AngleRightButton is not null)
            AngleRightButton.Clicked += OnAngleRightClicked;

        if (HairLeftButton is not null)
            HairLeftButton.Clicked += OnHairLeftClicked;

        if (HairRightButton is not null)
            HairRightButton.Clicked += OnHairRightClicked;

        //text fields
        NameField = CreateTextBox("NAME");
        PasswordField = CreateTextBox("PASSWD", 8);
        PasswordConfirmField = CreateTextBox("PASSWD2", 8);

        NameField?.ForegroundColor = LegendColors.White;
        NameField?.IsTabStop = true;
        
        PasswordField?.ForegroundColor = LegendColors.White;
        PasswordField?.IsTabStop = true;
        PasswordField?.IsMasked = true;
        
        PasswordConfirmField?.ForegroundColor = LegendColors.White;
        PasswordConfirmField?.IsTabStop = true;
        PasswordConfirmField?.IsMasked = true;

        if (NameField is not null)
            NameField.OnFocused += OnTextBoxFocused;

        if (PasswordField is not null)
            PasswordField.OnFocused += OnTextBoxFocused;

        if (PasswordConfirmField is not null)
            PasswordConfirmField.OnFocused += OnTextBoxFocused;

        //gender toggles — these use hover images, not button press behavior.
        //create as uibuttons to get textures + position, then use custom draw logic.
        MaleToggleArea = CreateButton("Male");
        FemaleToggleArea = CreateButton("Female");

        if (MaleToggleArea is UIButton maleBtn)
        {
            MaleUnselected = maleBtn.NormalTexture;
            MaleSelected = maleBtn.PressedTexture;

            //prevent button click behavior — we handle clicks manually via panel onclick
            maleBtn.NormalTexture = null;
            maleBtn.PressedTexture = null;
            maleBtn.IsHitTestVisible = false;
        }

        if (FemaleToggleArea is UIButton femaleBtn)
        {
            FemaleUnselected = femaleBtn.NormalTexture;
            FemaleSelected = femaleBtn.PressedTexture;
            femaleBtn.NormalTexture = null;
            femaleBtn.PressedTexture = null;
            femaleBtn.IsHitTestVisible = false;
        }

        //character preview area (human rect — type 7, 0 images)
        var humanRect = GetRect("HUMAN");

        if (humanRect != Rectangle.Empty)
        {
            PreviewX = humanRect.X;
            PreviewY = humanRect.Y;
            PreviewWidth = humanRect.Width;
            PreviewHeight = humanRect.Height;
        }

        //hair color swatch (haircolor — type 7, 1 image)
        SwatchImage = CreateImage("HairColor");
    }

    public override void Dispose()
    {
        for (var i = 0; i < AnimFrameTextures.Length; i++)
        {
            AnimFrameTextures[i]
                ?.Dispose();
            AnimFrameTextures[i] = null;
        }

        MaleUnselected?.Dispose();
        MaleSelected?.Dispose();
        FemaleUnselected?.Dispose();
        FemaleSelected?.Dispose();
        Renderer.Dispose();

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);

        var sx = ScreenX;
        var sy = ScreenY;

        //gender toggle images (custom hover rendering)
        var maleTexture = MaleHovered ? MaleSelected : MaleUnselected;
        var femaleTexture = FemaleHovered ? FemaleSelected : FemaleUnselected;

        if (maleTexture is not null && MaleToggleArea is not null)
            DrawTexture(spriteBatch, maleTexture, new Vector2(sx + MaleToggleArea.X, sy + MaleToggleArea.Y), Color.White);

        if (femaleTexture is not null && FemaleToggleArea is not null)
            DrawTexture(spriteBatch, femaleTexture, new Vector2(sx + FemaleToggleArea.X, sy + FemaleToggleArea.Y), Color.White);

        //character preview — composite texture is padded by LAYER_OFFSET_PADDING on each side,
        //so the body center within the texture is at CANVAS_CENTER_X, not BODY_CENTER_X.
        if (AnimFrameTextures[WalkFrame] is { } currentFrame)
        {
            var centerX = sx + PreviewX + PreviewWidth / 2 - AislingRenderer.CANVAS_CENTER_X;
            var centerY = sy + PreviewY + PreviewHeight / 2 - AislingRenderer.BODY_CENTER_Y;
            DrawTexture(spriteBatch, currentFrame, new Vector2(centerX, centerY), Color.White);
        }
    }

    private void HandleSwatchClick(int mouseX, int mouseY)
    {
        if (SwatchImage is null || !SwatchImage.ContainsPoint(mouseX, mouseY))
            return;

        var localX = mouseX - SwatchImage.ScreenX;
        var localY = mouseY - SwatchImage.ScreenY;
        var cellWidth = SwatchImage.Width / HAIR_COLOR_COLUMNS;
        var cellHeight = SwatchImage.Height / HAIR_COLOR_ROWS;

        if ((cellWidth <= 0) || (cellHeight <= 0))
            return;

        var col = Math.Clamp(localX / cellWidth, 0, HAIR_COLOR_COLUMNS - 1);
        var row = Math.Clamp(localY / cellHeight, 0, HAIR_COLOR_ROWS - 1);
        var cellIndex = row * HAIR_COLOR_COLUMNS + col;

        if (cellIndex >= SWATCH_COLOR_MAP.Length)
            return;

        SelectedHairColor = SWATCH_COLOR_MAP[cellIndex];
        PreviewDirty = true;
    }

    public override void Hide()
    {
        if (NameField is not null)
        {
            NameField.IsFocused = false;
            NameField.Text = string.Empty;
        }

        if (PasswordField is not null)
        {
            PasswordField.IsFocused = false;
            PasswordField.Text = string.Empty;
        }

        if (PasswordConfirmField is not null)
        {
            PasswordConfirmField.IsFocused = false;
            PasswordConfirmField.Text = string.Empty;
        }

        base.Hide();
    }

    private void OnAngleLeftClicked()
    {
        SelectedDirection = SelectedDirection switch
        {
            0 => 1,
            1 => 2,
            2 => 3,
            3 => 0,
            _ => 2
        };
        PreviewDirty = true;
    }

    private void OnAngleRightClicked()
    {
        SelectedDirection = SelectedDirection switch
        {
            0 => 3,
            1 => 0,
            2 => 1,
            _ => 2
        };
        PreviewDirty = true;
    }

    public event CancelHandler? OnCancel;

    private void OnHairLeftClicked()
    {
        var maxStyle = SelectedGender == Gender.Male ? MAX_MALE_HAIR_STYLE : MAX_FEMALE_HAIR_STYLE;
        SelectedHairStyle = SelectedHairStyle <= 1 ? (byte)maxStyle : (byte)(SelectedHairStyle - 1);
        PreviewDirty = true;
    }

    private void OnHairRightClicked()
    {
        var maxStyle = SelectedGender == Gender.Male ? MAX_MALE_HAIR_STYLE : MAX_FEMALE_HAIR_STYLE;
        SelectedHairStyle = SelectedHairStyle >= maxStyle ? (byte)1 : (byte)(SelectedHairStyle + 1);
        PreviewDirty = true;
    }

    public event OkHandler? OnOk;

    private void OnTextBoxFocused(UITextBox focused)
    {
        if (NameField is not null && (focused != NameField))
            NameField.IsFocused = false;

        if (PasswordField is not null && (focused != PasswordField))
            PasswordField.IsFocused = false;

        if (PasswordConfirmField is not null && (focused != PasswordConfirmField))
            PasswordConfirmField.IsFocused = false;
    }

    private void RenderWalkFrames()
    {
        for (var i = 0; i < ANIM_FRAME_COUNT; i++)
        {
            AnimFrameTextures[i]
                ?.Dispose();

            AnimFrameTextures[i] = Renderer.RenderPreview(
                SelectedGender,
                SelectedHairStyle,
                SelectedHairColor,
                SelectedDirection,
                i);
        }
    }

    private void SetGender(Gender gender)
    {
        if (SelectedGender == gender)
            return;

        SelectedGender = gender;
        var maxStyle = gender == Gender.Male ? MAX_MALE_HAIR_STYLE : MAX_FEMALE_HAIR_STYLE;

        if (SelectedHairStyle > maxStyle)
            SelectedHairStyle = (byte)maxStyle;

        PreviewDirty = true;
    }

    public override void Show()
    {
        if (NameField is not null)
        {
            NameField.Text = string.Empty;
            NameField.IsFocused = true;
        }

        if (PasswordField is not null)
        {
            PasswordField.Text = string.Empty;
            PasswordField.IsFocused = false;
        }

        if (PasswordConfirmField is not null)
        {
            PasswordConfirmField.Text = string.Empty;
            PasswordConfirmField.IsFocused = false;
        }

        SelectedGender = Gender.Male;
        SelectedHairStyle = 1;
        SelectedHairColor = DisplayColor.Default;
        SelectedDirection = 1;
        WalkFrame = 0;
        WalkTimer = 0;
        PreviewDirty = true;
        base.Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Tab:
                //cycle focus: name → password → passwordconfirm → name
                if (NameField?.IsFocused == true)
                {
                    NameField.IsFocused = false;
                    PasswordField?.IsFocused = true;
                } else if (PasswordField?.IsFocused == true)
                {
                    PasswordField.IsFocused = false;
                    PasswordConfirmField?.IsFocused = true;
                } else
                {
                    PasswordConfirmField?.IsFocused = false;
                    NameField?.IsFocused = true;
                }

                e.Handled = true;

                break;

            case Keys.Enter:
                OkButton?.PerformClick();
                e.Handled = true;

                break;

            case Keys.Escape:
                CancelButton?.PerformClick();
                e.Handled = true;

                break;
        }
    }

    public override void OnMouseLeave()
    {
        MaleHovered = false;
        FemaleHovered = false;
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        MaleHovered = MaleToggleArea?.ContainsPoint(e.ScreenX, e.ScreenY) == true;
        FemaleHovered = FemaleToggleArea?.ContainsPoint(e.ScreenX, e.ScreenY) == true;
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        if (MaleHovered)
            SetGender(Gender.Male);
        else if (FemaleHovered)
            SetGender(Gender.Female);

        HandleSwatchClick(e.ScreenX, e.ScreenY);
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible)
            return;

        //pre-render walk frames when appearance changes
        if (PreviewDirty)
        {
            RenderWalkFrames();
            PreviewDirty = false;
        }

        //advance walk animation
        WalkTimer += (float)gameTime.ElapsedGameTime.TotalMilliseconds;

        if (WalkTimer >= WALK_FRAME_INTERVAL_MS)
        {
            WalkTimer -= WALK_FRAME_INTERVAL_MS;
            WalkFrame = (WalkFrame + 1) % ANIM_FRAME_COUNT;
        }
    }
}