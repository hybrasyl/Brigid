#region
using Brigid.Controls.Components;
using DALib.Networking.Packets.Server;
using Microsoft.Xna.Framework;
#endregion

namespace Brigid.Controls.LobbyLogin;

public sealed class ServerSelectControl : PrefabPanel
{
    private const int ROW_HEIGHT = 20;
    private const int FIRST_ROW_Y = 37;
    private const int NAME_X = 28;
    private const int DESC_X = 108;
    private List<(UILabel Name, UILabel Description)> ServerLabels = [];

    private IList<ServerEntry> Servers = [];

    public ServerSelectControl()
        : base("_nsvr")
    {
        Name = "ServerSelect";
        Visible = false;
    }

    public event ServerSelectedHandler? OnServerSelected;

    public void SetServers(IList<ServerEntry> entries)
    {
        //remove previous server labels
        foreach ((var nameLabel, var descLabel) in ServerLabels)
        {
            RemoveChild(nameLabel.Name);
            RemoveChild(descLabel.Name);
        }

        Servers = entries;
        ServerLabels = [];

        for (var i = 0; i < entries.Count; i++)
        {
            var server = entries[i];

            var nameLabel = new UILabel
            {
                Name = $"ServerName{i}",
                X = NAME_X,
                Y = FIRST_ROW_Y + i * ROW_HEIGHT,
                Width = DESC_X - NAME_X,
                Height = ROW_HEIGHT,
                Text = server.Name,
                ForegroundColor = LegendColors.White,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            var descLabel = new UILabel
            {
                Name = $"ServerDesc{i}",
                X = DESC_X,
                Y = FIRST_ROW_Y + i * ROW_HEIGHT,
                Width = Width - DESC_X,
                Height = ROW_HEIGHT,
                Text = string.Empty,
                ForegroundColor = Color.LightGray,
                PaddingLeft = 0,
                PaddingTop = 0
            };

            AddChild(nameLabel);
            AddChild(descLabel);
            ServerLabels.Add((nameLabel, descLabel));
        }
    }

    public override void OnClick(ClickEvent e)
    {
        if (e.Button != MouseButton.Left)
            return;

        var panelScreenX = ScreenX;
        var panelScreenY = ScreenY;

        for (var i = 0; i < Servers.Count; i++)
        {
            var rowY = panelScreenY + FIRST_ROW_Y + i * ROW_HEIGHT;

            if ((e.ScreenX >= (panelScreenX + NAME_X))
                && (e.ScreenX < (panelScreenX + Width))
                && (e.ScreenY >= rowY)
                && (e.ScreenY < (rowY + ROW_HEIGHT)))
            {
                OnServerSelected?.Invoke(Servers[i].Id);
                e.Handled = true;

                return;
            }
        }
    }
}