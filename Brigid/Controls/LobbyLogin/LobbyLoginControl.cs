#region
using Brigid.Controls.Components;
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
    public LogoImage? AnimatedLogo { get; }
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

        //animated logo — logo control has 20 frames. create as static image first,
        //then replace with LogoImage using the prefab's frames.
        var logoImage = CreateImage("LOGO");

        if (logoImage is not null)
        {
            var logoPrefab = PrefabSet["LOGO"];
            var cache = UiRenderer.Instance!;
            var animFrames = new Texture2D[logoPrefab.Images.Count];

            for (var i = 0; i < logoPrefab.Images.Count; i++)
                animFrames[i] = cache.GetPrefabTexture(PrefabSet.Name, "LOGO", i);

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
        VersionLabel?.Text = $"Brigid v{UpdateChecker.GetCurrentVersion()}";
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
