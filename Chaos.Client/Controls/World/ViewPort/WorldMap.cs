#region
using Chaos.Client.Controls.Components;
using Chaos.Client.Networking;
using Chaos.Networking.Entities.Server;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
#endregion

namespace Chaos.Client.Controls.World.ViewPort;

/// <summary>
///     Full-screen field map overlay. Displays a background image with clickable destination nodes. Shown when the server
///     sends a WorldMap (SFieldMap) packet. Nodes are positioned at screen coordinates provided by the server. Clicking a
///     node sends a world map click response. Escape closes.
/// </summary>
public sealed class WorldMap : UIPanel
{
    private readonly ConnectionManager Connection;
    private readonly List<WorldMapNode> NodeControls = [];
    private Texture2D? BackgroundTexture;
    private int HoveredNodeIndex = -1;

    public WorldMap(ConnectionManager connection)
    {
        Connection = connection;
        Width = 640;
        Height = 480;
        X = 0;
        Y = 0;
        Visible = false;
        ZIndex = 1;
    }

    private void ClearBackground()
    {
        BackgroundTexture?.Dispose();
        BackgroundTexture = null;
    }

    private void ClearNodes()
    {
        foreach (var control in NodeControls)
            control.Dispose();

        NodeControls.Clear();
    }

    /// <inheritdoc />
    public override void Dispose()
    {
        ClearNodes();
        ClearBackground();
        base.Dispose();
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        //draw background image
        if (BackgroundTexture is not null)
            spriteBatch.Draw(
                BackgroundTexture,
                new Rectangle(
                    0,
                    0,
                    640,
                    480),
                Color.White);

        //draw node controls
        foreach (var control in NodeControls)
            control.Draw(spriteBatch);
    }

    public void HideMap()
    {
        Visible = false;
        HoveredNodeIndex = -1;
        ClearNodes();
        ClearBackground();
    }

    private Texture2D LoadFieldImage(string fieldName) => UiRenderer.Instance!.GetFieldImage(fieldName);

    public void Show(WorldMapArgs args)
    {
        ClearNodes();
        ClearBackground();

        BackgroundTexture = LoadFieldImage(args.FieldName);
        HoveredNodeIndex = -1;

        var nodes = args.Nodes.ToList();

        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];

            var control = new WorldMapNode(
                i,
                node.Text,
                node.MapId,
                node.DestinationPoint.X,
                node.DestinationPoint.Y,
                node.CheckSum)
            {
                X = node.ScreenPosition.X,
                Y = node.ScreenPosition.Y
            };

            NodeControls.Add(control);
        }

        Visible = true;
    }

    public override void OnKeyDown(KeyDownEvent e)
    {
        if (e.Key == Keys.Escape)
        {
            HideMap();
            e.Handled = true;
        }
    }

    public override void OnMouseMove(MouseMoveEvent e)
    {
        var previousIndex = HoveredNodeIndex;
        HoveredNodeIndex = -1;

        for (var i = 0; i < NodeControls.Count; i++)
        {
            var node = NodeControls[i];
            var nx = node.ScreenX;
            var ny = node.ScreenY;

            if ((e.ScreenX >= nx) && (e.ScreenX < (nx + node.Width)) && (e.ScreenY >= ny) && (e.ScreenY < (ny + node.Height)))
            {
                HoveredNodeIndex = i;

                break;
            }
        }

        if (HoveredNodeIndex != previousIndex)
        {
            if (previousIndex >= 0)
                NodeControls[previousIndex].SetHovered(false);

            if (HoveredNodeIndex >= 0)
                NodeControls[HoveredNodeIndex].SetHovered(true);
        }
    }

    public override void OnMouseLeave()
    {
        if (HoveredNodeIndex >= 0)
        {
            NodeControls[HoveredNodeIndex].SetHovered(false);
            HoveredNodeIndex = -1;
        }
    }

    public override void OnClick(ClickEvent e)
    {
        base.OnClick(e);

        if (HoveredNodeIndex >= 0)
        {
            var control = NodeControls[HoveredNodeIndex];

            Connection.ClickWorldMapNode(
                control.MapId,
                control.DestX,
                control.DestY,
                control.CheckSum);

            //teardown happens in WorldScreen.HandleMapInfo when the destination map's data lands —
            //keeping the worldmap visible across the click→warp latency masks the brief moment where
            //the source map would otherwise be visible underneath.
            e.Handled = true;
        }
    }
}