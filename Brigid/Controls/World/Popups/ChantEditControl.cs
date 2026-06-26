#region
using Brigid.Controls.Components;
using Brigid.Extensions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     Chant line editor popup using lssbook prefab. Shows skill/spell icon, name, level, and text inputs for chant lines.
///     Skills have 1 line, spells have one per CastLine. OK saves, Cancel discards. Uses butt001.epf for buttons.
///     Uses 3-part composite background: TopImage + tiled MidImage + BotImage.
/// </summary>
public sealed class ChantEditControl : PrefabPanel
{
    //butt001.epf frame indices
    private const int OK_NORMAL = 15;
    private const int OK_PRESSED = 16;
    private const int CANCEL_NORMAL = 21;
    private const int CANCEL_PRESSED = 22;

    private const int TOP_HEIGHT = 89;
    private const int MID_HEIGHT = 25;
    private const int BOT_HEIGHT = 45;
    private const int PANEL_WIDTH = 246;
    private const int TEXT_X = 26;
    private const int TEXT_WIDTH = 196;
    private const int TEXT_HEIGHT = 16;
    private const int MAX_LINES = 10;
    private readonly UIImage? BotImage;
    private readonly UIButton? CancelButton;

    private readonly UIImage? Icon;
    private readonly UILabel? LevelLabel;
    private readonly Texture2D? MidTexture;
    private readonly UILabel? NameLabel;

    private readonly UIButton? OkButton;

    private readonly UITextBox[] TextInputs;
    private byte EditingSlot;
    private bool IsSpell;
    private int LineCount;

    public ChantEditControl()
        : base("lssbook", false)
    {
        Name = "ChantEdit";
        Visible = false;
        UsesControlStack = true;

        Width = PANEL_WIDTH;
        Height = TOP_HEIGHT + MID_HEIGHT + BOT_HEIGHT;

        //3-part composite background: topimage, tiled midimage, botimage
        CreateImage("TopImage");
        BotImage = CreateImage("BotImage");

        //midimage is tiled vertically — store the texture directly instead of using a uiimage child
        if (PrefabSet.Contains("MidImage"))
        {
            var midPrefab = PrefabSet["MidImage"];

            if (midPrefab.Images.Count > 0)
                MidTexture = UiRenderer.Instance!.GetPrefabTexture(PrefabSet.Name, "MidImage", 0);
        }

        //get ok/cancel rects from prefab for positioning, then create custom buttons with butt001.epf
        var okRect = GetRect("OK");
        var cancelRect = GetRect("Cancel");

        OkButton = CreateButtonWithEpf(
            "OkBtn",
            OK_NORMAL,
            OK_PRESSED,
            okRect != Rectangle.Empty ? okRect.X : 12,
            okRect != Rectangle.Empty ? okRect.Y : 116);

        CancelButton = CreateButtonWithEpf(
            "CancelBtn",
            CANCEL_NORMAL,
            CANCEL_PRESSED,
            cancelRect != Rectangle.Empty ? cancelRect.X : 152,
            cancelRect != Rectangle.Empty ? cancelRect.Y : 116);

        if (OkButton is not null)
            OkButton.Clicked += Confirm;

        if (CancelButton is not null)
            CancelButton.Clicked += Cancel;

        NameLabel = CreateLabel("Name");
        LevelLabel = CreateLabel("Level");
        Icon = CreateImage("Icon");

        //pre-create all text boxes — shown/hidden based on line count
        TextInputs = new UITextBox[MAX_LINES];

        for (var i = 0; i < MAX_LINES; i++)
        {
            TextInputs[i] = new UITextBox
            {
                Name = $"ChantLine{i}",
                X = TEXT_X,
                Y = TOP_HEIGHT + 2 + i * MID_HEIGHT,
                Width = TEXT_WIDTH,
                Height = TEXT_HEIGHT,
                MaxLength = 32,
                Visible = false,
                ZIndex = 1
            };

            AddChild(TextInputs[i]);
        }
    }

    private void Cancel() => Hide();

    private void Confirm()
    {
        var chants = new string[LineCount];

        for (var i = 0; i < LineCount; i++)
            chants[i] = TextInputs[i].Text;

        OnChantSet?.Invoke(EditingSlot, chants, IsSpell);
        Hide();
    }

    private UIButton CreateButtonWithEpf(
        string name,
        int normalFrame,
        int pressedFrame,
        int x,
        int y)
    {
        var cache = UiRenderer.Instance!;
        var normalTex = cache.GetEpfTexture("butt001.epf", normalFrame);
        var pressedTex = cache.GetEpfTexture("butt001.epf", pressedFrame);

        var button = new UIButton
        {
            Name = name,
            X = x,
            Y = y,
            Width = normalTex.Width,
            Height = normalTex.Height,
            NormalTexture = normalTex,
            PressedTexture = pressedTex,
            ZIndex = 1
        };

        AddChild(button);

        return button;
    }

    public override void Dispose()
    {
        Icon?.Texture = null;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        //tile midimage between topimage and botimage
        if (MidTexture is not null && (LineCount > 0))
        {
            var midStartY = ScreenY + TOP_HEIGHT;

            for (var i = 0; i < LineCount; i++)
                DrawTexture(
                    spriteBatch,
                    MidTexture,
                    new Vector2(ScreenX, midStartY + i * MID_HEIGHT),
                    Color.White);
        }

        //draw children (topimage, botimage, text inputs, buttons, labels, icon)
        base.Draw(spriteBatch);
    }

    public override void Hide()
    {
        Icon?.Texture = null;

        base.Hide();
    }

    /// <summary>
    ///     Fired when OK is pressed. Parameters: slot (1-based), chant lines array, isSpell.
    /// </summary>
    public event ChantSetHandler? OnChantSet;

    public void Show(
        byte slot,
        string name,
        string level,
        Texture2D? icon,
        string[] chants,
        int lineCount,
        bool isSpell)
    {
        EditingSlot = slot;
        IsSpell = isSpell;
        LineCount = lineCount;

        NameLabel?.Text = name;
        LevelLabel?.Text = level;

        Icon?.Texture = icon;

        //show/hide text inputs based on line count
        for (var i = 0; i < MAX_LINES; i++)
            if (i < LineCount)
            {
                TextInputs[i].Text = i < chants.Length ? chants[i] : string.Empty;
                TextInputs[i].Visible = true;
                TextInputs[i].IsFocused = i == 0;
            } else
            {
                TextInputs[i].Text = string.Empty;
                TextInputs[i].Visible = false;
                TextInputs[i].IsFocused = false;
            }

        //reposition bot image and buttons for the line count
        var totalMidHeight = LineCount * MID_HEIGHT;
        var botY = TOP_HEIGHT + totalMidHeight;

        BotImage?.Y = botY;

        OkButton?.Y = botY + 2;

        CancelButton?.Y = botY + 2;

        Height = TOP_HEIGHT + totalMidHeight + BOT_HEIGHT;
        this.CenterOnScreen();

        base.Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Escape:
                Cancel();
                e.Handled = true;

                break;

            case Keys.Enter:
                Confirm();
                e.Handled = true;

                break;

            case Keys.Tab when LineCount > 1:
                for (var i = 0; i < LineCount; i++)
                    if (TextInputs[i].IsFocused)
                    {
                        TextInputs[i].IsFocused = false;
                        TextInputs[(i + 1) % LineCount].IsFocused = true;

                        break;
                    }

                e.Handled = true;

                break;
        }
    }
}