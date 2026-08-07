#region
using Brigid.Controls.Components;
using Brigid.Data.Models;
using Brigid.Rendering;
using Brigid.Systems;
using Brigid.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.LobbyLogin;

public sealed class LobbyLoginControl : PrefabPanel
{
    //config button — a hand-placed primitive (the _nstart prefab has no slot for it), styled as a discreet dark rect
    //so it reads as a button without competing with the prefab-art menu buttons.
    private const int CONFIG_BTN_WIDTH = 58;
    private const int CONFIG_BTN_HEIGHT = 18;
    private const int CONFIG_BTN_MARGIN = 6;

    private static readonly Color ConfigButtonBg = new(36, 36, 40, 220);
    private static readonly Color ConfigButtonHoverBg = new(70, 70, 80, 235);
    private static readonly Color ConfigButtonBorder = new(170, 170, 180, 255);

    public LogoImage? AnimatedLogo { get; }

    //opens the launcher/config screen (server, asset folder, resolution). Always enabled — it is local and does not
    //depend on the lobby connection, so it is deliberately left out of SetButtonsEnabled.
    public UIButton? ConfigButton { get; }
    public UIButton? ContinueButton { get; }
    public UIButton? CreditButton { get; }
    public UIButton? ExitButton { get; }
    public UIButton? HomepageButton { get; }
    public UIButton? PasswordButton { get; }
    public UIButton? SubmitCreateButton { get; }
    public LinkLabel? UpdateNotice { get; }
    public UILabel? VersionLabel { get; }

    public LobbyLoginControl()
        : base("_nstart", false)
    {
        Name = "StartScreen";
        X = 0;
        Y = 0;

        //buttons
        SubmitCreateButton = CreateButton("Create");
        ContinueButton = CreateButton("Continue");
        PasswordButton = CreateButton("Password");
        CreditButton = CreateButton("Credit");
        HomepageButton = CreateButton("Homepage");
        ExitButton = CreateButton("Exit");

        //start screen buttons use hover effect instead of press
        UIButton?[] allButtons =
        [
            SubmitCreateButton,
            ContinueButton,
            PasswordButton,
            CreditButton,
            HomepageButton,
            ExitButton
        ];

        foreach (var btn in allButtons)
        {
            if (btn is null)
                continue;

            btn.HoverTexture = btn.PressedTexture;
            btn.PressedTexture = null;
            btn.Enabled = false;
        }

        //animated logo. LOGO's two <IMAGE> entries are the endpoints of the flame loop, not two visual states, so
        //the span is expanded here rather than read off the prefab's rendered images — drawing those two alone
        //blinks the ring on and off. Create as static image first, then replace with LogoImage.
        var logoImage = CreateImage("LOGO");

        if (logoImage is not null)
        {
            var cache = UiRenderer.Instance!;
            (var logoSpf, var firstFrame, var frameCount) = ControlPrefab.ResolveAnimationSpan(PrefabSet["LOGO"].Control.Images);
            var animFrames = new Texture2D[frameCount];

            for (var i = 0; i < frameCount; i++)
                animFrames[i] = cache.GetSpfTexture(logoSpf, firstFrame + i);

            AnimatedLogo = new LogoImage
            {
                Name = "AnimatedLogo",
                X = logoImage.X,
                Y = logoImage.Y,
                Width = logoImage.Width,
                Height = logoImage.Height,
                Frames = animFrames,
                FrameIntervalMs = 150,
                Looping = true,
                PingPong = true
            };

            //replace the static image with the animated one
            Children.Remove(logoImage);
            logoImage.Dispose();
            AddChild(AnimatedLogo);
        }

        //version label — type 7, 0 images
        VersionLabel = CreateLabel("Version", HorizontalAlignment.Right);
        VersionLabel?.Text = $"Brigid v{VersionInfo.Display}";
        VersionLabel?.ForegroundColor = Color.Blue;

        //update notice — stacked above the version label, populated when the startup
        //release check (UpdateChecker) finds a newer tag. Constructed as a right-edge anchor
        //(aligned with the version label); Update() sets the real X/Width from the measured text.
        if (VersionLabel is not null)
        {
            UpdateNotice = new LinkLabel
            {
                Name = "UpdateNotice",
                X = VersionLabel.X,
                Y = VersionLabel.Y - VersionLabel.Height,
                Width = VersionLabel.Width,
                Height = VersionLabel.Height,
                HorizontalAlignment = HorizontalAlignment.Right,
                ForegroundColor = Color.Red,
                HoverColor = Color.OrangeRed,
                TextStyle = FontStyle.Bold,
                Visible = false
            };

            UpdateNotice.Clicked += () => Browser.Open(UpdateChecker.Available?.Url ?? string.Empty);
            AddChild(UpdateNotice);
        }

        //config button — bottom-left corner. Left always enabled (unlike the prefab menu buttons, which gate on the
        //lobby connection) so the launcher/config screen is reachable from the main menu at any time.
        ConfigButton = new UIButton
        {
            Name = "ConfigButton",
            X = CONFIG_BTN_MARGIN,
            Y = Height - CONFIG_BTN_HEIGHT - CONFIG_BTN_MARGIN,
            Width = CONFIG_BTN_WIDTH,
            Height = CONFIG_BTN_HEIGHT,
            BackgroundColor = ConfigButtonBg,
            BorderColor = ConfigButtonBorder
        };

        ConfigButton.Hovered += _ => ConfigButton.BackgroundColor = ConfigButtonHoverBg;
        ConfigButton.Unhovered += _ => ConfigButton.BackgroundColor = ConfigButtonBg;
        AddChild(ConfigButton);

        AddChild(
            new UILabel
            {
                Name = "ConfigButtonLabel",
                X = ConfigButton.X,
                Y = ConfigButton.Y + 3,
                Width = ConfigButton.Width,
                Height = ConfigButton.Height - 3,
                Text = "Config",
                ForegroundColor = Color.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                IsHitTestVisible = false
            });
    }

    public override void Update(GameTime gameTime)
    {
        base.Update(gameTime);

        //surface the async release-check result once it lands
        if (UpdateNotice is { Visible: false } notice && UpdateChecker.Available is { } update)
        {
            notice.Text = $"{update.Tag} available!";

            //shrink the hit box to the rendered text (right edge fixed) so clicks on the
            //blank strip to the left don't open the browser
            var textWidth = notice.MeasureTextWidth() + 2;
            notice.X = notice.X + notice.Width - textWidth;
            notice.Width = textWidth;
            notice.Visible = true;
        }
    }

    public void EnableButtons() => SetButtonsEnabled(true);

    public void SetButtonsEnabled(bool enabled)
    {
        SubmitCreateButton?.SetEnabled(enabled);
        ContinueButton?.SetEnabled(enabled);
        PasswordButton?.SetEnabled(enabled);
        CreditButton?.SetEnabled(enabled);
        HomepageButton?.SetEnabled(enabled);
        ExitButton?.SetEnabled(enabled);
    }
}

file static class ButtonExtensions
{
    public static void SetEnabled(this UIButton? button, bool enabled) => button?.Enabled = enabled;
}
