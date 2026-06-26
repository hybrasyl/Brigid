#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.ViewModel;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     Group/party panel using _ngcdlg0 prefab. Displays group member slots with quit buttons.
///     When the player is the leader, quit buttons next to other members are enabled (kick).
///     BTN_OK closes the panel.
/// </summary>
public sealed class GroupTab : PrefabPanel
{
    private const int MAX_MEMBERS = 13;
    private const int ROW_HEIGHT = 22;
    private const int NAME_X = 53;
    private const int NAME_START_Y = 47;
    private const int QUIT_BTN_X = 156;
    private const int QUIT_BTN_START_Y = 42;
    private const int QUIT_BTN_WIDTH = 30;
    private const int QUIT_BTN_HEIGHT = 20;

    private readonly UILabel?[] MemberLabels = new UILabel?[MAX_MEMBERS];
    private readonly UIButton[] QuitButtons = new UIButton[MAX_MEMBERS];
    private bool Dirty;

    private GroupChangedHandler GroupChangedHandler { get; }

    public UIButton? OkButton { get; }

    public GroupTab()
        : base("_ngcdlg0", false)
    {
        Name = "Group";
        Visible = false;
        UsesControlStack = true;

        //position at top-left of screen
        X = 0;
        Y = 0;

        OkButton = CreateButton("BTN_OK");

        if (OkButton is not null)
            OkButton.Clicked += () =>
            {
                Hide();
                OnClose?.Invoke();
            };

        //quit button textures from b_btn0: 0=normal, 1=pressed, 2=disabled
        var cache = UiRenderer.Instance!;
        var pressedTexture = cache.GetPrefabTexture("_ngcdlg0", "B_BTN0", 1);
        var disabledTexture = cache.GetPrefabTexture("_ngcdlg0", "B_BTN0", 2);

        //create member name labels and quit buttons for each row
        for (var i = 0; i < MAX_MEMBERS; i++)
        {
            var controlName = $"USER{i}";
            var label = CreateLabel(controlName);

            if (label is not null)
                MemberLabels[i] = label;
            else
            {
                MemberLabels[i] = new UILabel
                {
                    Name = controlName,
                    X = NAME_X,
                    Y = NAME_START_Y + i * ROW_HEIGHT,
                    Width = 72,
                    Height = 12
                };

                AddChild(MemberLabels[i]!);
            }

            //quit/kick button for each row — disabled by default
            var quitButton = new UIButton
            {
                Name = $"QUIT{i}",
                X = QUIT_BTN_X,
                Y = QUIT_BTN_START_Y + i * ROW_HEIGHT,
                Width = QUIT_BTN_WIDTH,
                Height = QUIT_BTN_HEIGHT,
                NormalTexture = disabledTexture,
                PressedTexture = pressedTexture,
                Enabled = false
            };

            //capture index for the click handler
            var memberIndex = i;

            quitButton.Clicked += () =>
            {
                var members = WorldState.Group.Members;

                if (memberIndex < members.Count)
                    OnKick?.Invoke(members[memberIndex]);
            };

            QuitButtons[i] = quitButton;
            AddChild(quitButton);
        }

        GroupChangedHandler = () => Dirty = true;
        WorldState.Group.Changed += GroupChangedHandler;
        Dirty = true;
    }

    public override void Dispose()
    {
        WorldState.Group.Changed -= GroupChangedHandler;

        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        base.Draw(spriteBatch);
    }

    public event CloseHandler? OnClose;
    public event GroupKickHandler? OnKick;

    private void Refresh()
    {
        Dirty = false;
        var members = WorldState.Group.Members;
        var leaderName = WorldState.Group.LeaderName;
        var isLeader = WorldState.Group.IsLeader;
        var playerName = WorldState.PlayerName;

        var cache = UiRenderer.Instance!;
        var normalTexture = cache.GetPrefabTexture("_ngcdlg0", "B_BTN0", 0);
        var disabledTexture = cache.GetPrefabTexture("_ngcdlg0", "B_BTN0", 2);

        for (var i = 0; i < MAX_MEMBERS; i++)
        {
            if (MemberLabels[i] is not null)
            {
                if (i < members.Count)
                {
                    var isMemberLeader = leaderName is not null && members[i].EqualsI(leaderName);
                    MemberLabels[i]!.ForegroundColor = isMemberLeader ? LegendColors.White : TextColors.Default;
                    MemberLabels[i]!.Text = members[i];
                } else
                    MemberLabels[i]!.Text = string.Empty;
            }

            //enable quit button only for other members when we are the leader
            var isSelf = (i < members.Count)
                         && members[i]
                             .EqualsI(playerName);
            var canKick = isLeader && (i < members.Count) && !isSelf;

            QuitButtons[i].Enabled = canKick;
            QuitButtons[i].NormalTexture = canKick ? normalTexture : disabledTexture;
        }
    }

    public override void Update(GameTime gameTime)
    {
        if (!Visible || !Enabled)
            return;

        if (Dirty)
            Refresh();

        base.Update(gameTime);
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            Hide();
            OnClose?.Invoke();
            e.Handled = true;
        }
    }
}