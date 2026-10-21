#region
using Brigid.Collections;
using Brigid.Controls.Components;
using Brigid.Controls.Generic;
using Brigid.Definitions;
using Brigid.Rendering;
using Brigid.ViewModel;
using Chaos.Extensions.Common;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.World.Popups;

/// <summary>
///     The Members tab of the group window (BRIG-126 step 3c), in place of the legacy <c>_ngcdlg0</c> prefab. Up to 13
///     rows, one for each member of the group. A row shows the name, and "Leader" on the leader's row. When you are the
///     leader, the row of each other member has a Kick button. With no group, the tab says so. The tab shows the
///     <c>WorldState.Group</c> state; the host gives its size and place.
/// </summary>
public sealed class GroupTab : UIPanel
{
    public const int MAX_MEMBERS = 13;
    private const int ROW_H = 17;
    private const int KICK_W = 44;
    private const int LEADER_W = 64;
    private const int KICK_H = 16;

    /// <summary>The height of the tab: one row for each member.</summary>
    public const int HEIGHT = MAX_MEMBERS * ROW_H;

    private readonly Row[] Rows = new Row[MAX_MEMBERS];
    private readonly UILabel EmptyLabel;
    private readonly GroupChangedHandler GroupChangedHandler;
    private bool Dirty = true;

    public event GroupKickHandler? OnKick;

    public GroupTab(int width)
    {
        Width = width;
        Height = HEIGHT;
        IsPassThrough = true;

        for (var i = 0; i < MAX_MEMBERS; i++)
        {
            var row = new Row(width)
            {
                Name = $"row_{i}",
                Y = i * ROW_H
            };
            row.KickButton.Clicked += () => OnKick?.Invoke(row.NameLabel.Text);
            Rows[i] = row;
            AddChild(row);
        }

        EmptyLabel = new UILabel
        {
            Name = "empty",
            Width = width,
            Height = ROW_H,
            Text = "You are not in a group.",
            VerticalAlignment = VerticalAlignment.Center,
            PaddingLeft = 0,
            IsHitTestVisible = false
        };
        EmptyLabel.SetForegroundRole(ThemeRoles.TextDisabled);
        AddChild(EmptyLabel);

        GroupChangedHandler = () => Dirty = true;
        WorldState.Group.Changed += GroupChangedHandler;
    }

    /// <summary>The name of one row, and whether it has the Kick button and the leader mark. Internal for the tests.</summary>
    internal (string Name, bool CanKick, bool IsLeader) RowState(int index)
    {
        var row = Rows[index];

        return (row.Visible ? row.NameLabel.Text : string.Empty, row.Visible && row.KickButton.Visible, row.Visible && row.LeaderLabel.Visible);
    }

    /// <summary>The Kick button of one row. Internal for the tests.</summary>
    internal TextButton KickButton(int index) => Rows[index].KickButton;

    /// <summary>True when the tab says that you are not in a group. Internal for the tests.</summary>
    internal bool ShowsEmpty => EmptyLabel.Visible;

    public override void Dispose()
    {
        WorldState.Group.Changed -= GroupChangedHandler;

        base.Dispose();
    }

    public override void Update(GameTime gameTime)
    {
        if (Visible && Dirty)
            Refresh();

        base.Update(gameTime);
    }

    private void Refresh()
    {
        Dirty = false;

        var members = WorldState.Group.Members;
        var leaderName = WorldState.Group.LeaderName;
        var isLeader = WorldState.Group.IsLeader;

        for (var i = 0; i < MAX_MEMBERS; i++)
        {
            var row = Rows[i];
            row.Visible = i < members.Count;

            if (!row.Visible)
                continue;

            var name = members[i];
            var isMemberLeader = (leaderName is not null) && name.EqualsI(leaderName);

            row.NameLabel.Text = name;
            row.LeaderLabel.Visible = isMemberLeader;

            //only the leader kicks, and not themself
            row.KickButton.Visible = isLeader && !name.EqualsI(WorldState.PlayerName);
        }

        EmptyLabel.Visible = members.Count == 0;
    }

    //one member: the name on the left; the Kick button or the leader mark on the right
    private sealed class Row : UIPanel
    {
        public readonly UILabel NameLabel;
        public readonly UILabel LeaderLabel;
        public readonly TextButton KickButton;

        public Row(int width)
        {
            Width = width;
            Height = ROW_H;
            IsPassThrough = true;
            Visible = false;

            NameLabel = new UILabel
            {
                Name = "name",
                Width = width - LEADER_W - 4,
                Height = ROW_H,
                VerticalAlignment = VerticalAlignment.Center,
                PaddingLeft = 0,
                IsHitTestVisible = false
            };
            NameLabel.SetForegroundRole(ThemeRoles.Text);
            AddChild(NameLabel);

            LeaderLabel = new UILabel
            {
                Name = "leader",
                X = width - LEADER_W,
                Width = LEADER_W,
                Height = ROW_H,
                Text = "Leader",
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                PaddingRight = 0,
                Visible = false,
                IsHitTestVisible = false
            };
            LeaderLabel.SetForegroundRole(ThemeRoles.TextSelected);
            AddChild(LeaderLabel);

            KickButton = new TextButton("Kick", KICK_W, KICK_H)
            {
                Name = "kick",
                X = width - KICK_W,
                Y = (ROW_H - KICK_H) / 2,
                Visible = false
            };
            AddChild(KickButton);
        }
    }
}
