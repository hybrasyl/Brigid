#region
using Brigid.Controls.Components;
using Brigid.Controls.Scrolling;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.LobbyLogin;

public sealed class LoginNoticeControl : PrefabPanel
{
    private readonly UILabel? AgreementTextLabel;
    private readonly ScrollView? AgreementScroll;
    private readonly int TextAreaHeight;
    private readonly int TextAreaWidth;
    public UIButton? CancelButton { get; }

    public UIButton? OkButton { get; }

    public LoginNoticeControl()
        : base("_nagree")
    {
        Name = "AgreePanel";
        Visible = false;
        UsesControlStack = true;

        //banner title — _nagree's 'CONTROL' region (rect 198,7 95x12) is the plaque title slot.
        //The legacy client draws "Notification." here; the prefab carries no text of its own.
        var titleLabel = CreateLabel("CONTROL", HorizontalAlignment.Center);

        if (titleLabel is not null)
        {
            titleLabel.Text = "Notification";

            //baked nudge offset (F11 live-nudge): shift -17px to center the title on the banner plaque
            titleLabel.X -= 17;
        }

        //buttons — _nagree is one of the few prefabs with actual type 3 controls
        OkButton = CreateButton("OK");
        CancelButton = CreateButton("CANCEL");

        if (OkButton is not null)
            OkButton.Clicked += () => OnOk?.Invoke();

        if (CancelButton is not null)
            CancelButton.Clicked += () => OnCancel?.Invoke();

        //agreement text display region — type 7, 0 images
        var textRect = GetRect("AGREEMENTTEXT");

        if (textRect != Rectangle.Empty)
        {
            TextAreaWidth = textRect.Width - 25;
            TextAreaHeight = textRect.Height;

            AgreementTextLabel = new UILabel
            {
                Name = "AgreementText",
                X = 0,
                Y = 0,
                Width = TextAreaWidth,
                Height = TextAreaHeight,
                PaddingLeft = 0,
                PaddingTop = 0,
                WordWrap = true
            };

            //label hosted in a ScrollView that owns the bar and the wheel handling for the agreement text area
            AgreementScroll = new ScrollView
            {
                Name = "AgreementScroll",
                X = textRect.X,
                Y = textRect.Y,
                Width = textRect.Width,
                Height = TextAreaHeight,
                Visible = false
            };

            AgreementScroll.AddChild(AgreementTextLabel);
            AddChild(AgreementScroll);
            AgreementScroll.SetSource(new LabelScrollSource(AgreementTextLabel));
        }
    }

    public override void Hide()
    {
        AgreementScroll?.Visible = false;

        base.Hide();
    }

    public event CancelHandler? OnCancel;

    public event OkHandler? OnOk;

    public void Show(string agreementText)
    {
        if ((AgreementTextLabel is not null) && (AgreementScroll is not null))
        {
            AgreementTextLabel.ForegroundColor = TextColors.Default;
            AgreementTextLabel.Text = agreementText;
            AgreementScroll.Visible = true;
            AgreementScroll.Sync();
            AgreementScroll.ScrollToStart();
        }

        base.Show();
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            case Keys.Enter:
                OkButton?.PerformClick();
                e.Handled = true;

                break;

            case Keys.Escape:
                //intentionally blocked — escape is a no-op during eula display
                e.Handled = true;

                break;
        }
    }
}