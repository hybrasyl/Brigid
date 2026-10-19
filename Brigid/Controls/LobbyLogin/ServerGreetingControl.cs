#region
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Controls.Scrolling;
using Brigid.Definitions;
using Brigid.Rendering;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.LobbyLogin;

/// <summary>
///     The server greeting (the login notice) as a primitive modal (BRIG-107). It replaces the prefab
///     <see cref="LoginNoticeControl" />. The body is the notice text in a scrolling label, with the <c>{=x}</c> colour
///     codes; the screen normalizes the retail text before <see cref="Show(string)" />.
///     <para>
///         OK raises <see cref="Accepted" />, and the menu buttons then work. Cancel raises <see cref="Declined" />, and the
///         client exits. Enter is OK. Escape does nothing: the player must choose.
///     </para>
/// </summary>
public sealed class ServerGreetingControl : CenteredModalPanel
{
    private const int PANEL_W = 460;
    private const int PANEL_H = 340;
    private const int ACTION_W = 64;

    private readonly UILabel Body;
    private readonly ScrollView BodyScroll;

    //the FontEngine generation the scroll range was read at; a resize or a face change wraps the text again
    private int ScrollGeneration = -1;

    public TextButton OkButton { get; }
    public TextButton CancelButton { get; }

    /// <summary>Raised when the player accepts the greeting.</summary>
    public event Action? Accepted;

    /// <summary>Raised when the player declines the greeting.</summary>
    public event Action? Declined;

    public ServerGreetingControl()
        //the greeting is a child of the lobby root, not of the menu, so it sets the theme itself
        : base("Notification", PANEL_W, PANEL_H, useTheme: true)
    {
        Name = "servergreeting";
        IsModal = true;

        //the greeting has no Close: the player accepts or declines
        CloseButton.Visible = false;

        CancelButton = AddBottomBarButton("Cancel", ACTION_W, Decline, "cancel");
        OkButton = AddBottomBarButton("OK", ACTION_W, Accept, "ok");

        BodyScroll = new ScrollView { Name = "body" };

        Body = new UILabel
        {
            Name = "text",
            X = 0,
            Y = 0,
            WordWrap = true,
            IsHitTestVisible = false
        };

        BodyScroll.AddChild(Body);
        AddChild(BodyScroll);
        LayoutContent();
        BodyScroll.SetSource(new LabelScrollSource(Body));
    }

    //the text fills the content area
    protected override void LayoutContent()
    {
        var content = ContentBounds;

        BodyScroll.X = content.X;
        BodyScroll.Y = content.Y + PADDING;
        BodyScroll.Width = content.Width;
        BodyScroll.Height = content.Height - 2 * PADDING;

        Body.Width = BodyScroll.ContentWidth - PADDING;
        Body.Height = BodyScroll.Height;

        //a new width wraps the text again, and the scroll range follows
        Body.Text = Body.Text;
        BodyScroll.Sync();
    }

    /// <summary>Shows the greeting with the notice text, scrolled to the top.</summary>
    public void Show(string text)
    {
        Body.Text = text;
        BodyScroll.Sync();
        BodyScroll.ScrollToStart();
        ScrollGeneration = FontEngine.Instance.Generation;

        base.Show();
    }

    public override void Update(GameTime gameTime)
    {
        //a new wrap changes the number of lines, so the scroll range is read again
        if (Visible && (ScrollGeneration != FontEngine.Instance.Generation))
        {
            Body.Text = Body.Text;
            BodyScroll.Sync();
            ScrollGeneration = FontEngine.Instance.Generation;
        }

        base.Update(gameTime);
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        Body.ForegroundColor = ThemeColor(ThemeRoles.Text, TextColors.Default);

        base.Draw(spriteBatch);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        switch (e.Key)
        {
            //Alt+Enter changes the window size; it must not accept the greeting
            case Keys.Enter when !e.Alt:
                Accept();
                e.Handled = true;

                return;

            //the base closes on Escape; the greeting needs an answer
            case Keys.Escape:
                e.Handled = true;

                return;
        }

        base.OnKeyDown(e);
    }

    //the wheel scrolls the text from anywhere on the panel, not only over the text
    public override void OnMouseScroll(MouseScrollEvent e)
    {
        BodyScroll.OnMouseScroll(e);
        e.Handled = true;
    }

    private void Accept()
    {
        Hide();
        Accepted?.Invoke();
    }

    private void Decline()
    {
        Hide();
        Declined?.Invoke();
    }
}
