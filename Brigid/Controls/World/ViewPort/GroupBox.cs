#region
using Brigid.Controls.Components;
using Brigid.Rendering.Utility;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.ViewPort;

/// <summary>
///     Group recruitment text banner rendered as a gc_pane2.spf panel frame with centered text. Displayed above aisling
///     entities that have an active GroupBox. Always visible while text is non-empty.
/// </summary>
public sealed class GroupBox : UIPanel
{
    //gc_pane2.spf is 103x19 — wooden frame with teal interior
    public const int PANEL_WIDTH = 103;
    public const int PANEL_HEIGHT = 19;

    //title rect from lgcpane.txt: (3,3)-(100,16), 97x13 text area
    private const int TITLE_X = 3;
    private const int TITLE_Y = 3;
    private const int TITLE_WIDTH = 97;
    private const int TITLE_HEIGHT = 13;

    private static readonly Color NormalFillColor = new(
        0,
        0,
        0,
        128);

    private static readonly Color HoverFillColor = new(
        0,
        0,
        120,
        90);

    private static Texture2D? NormalPanelTexture;
    private static Texture2D? HoverPanelTexture;
    private static bool PanelTexturesInitialized;

    private readonly UILabel Label;
    public bool IsHovered { get; set; }

    public uint EntityId { get; }

    public GroupBox(uint entityId)
    {
        EntityId = entityId;
        Width = PANEL_WIDTH;
        Height = PANEL_HEIGHT;

        Label = new UILabel
        {
            X = TITLE_X,
            Y = TITLE_Y,
            Width = TITLE_WIDTH,
            Height = TITLE_HEIGHT,
            PaddingLeft = 1,
            PaddingTop = 1,
            HorizontalAlignment = HorizontalAlignment.Center,
            ForegroundColor = new Color(187, 187, 187)
        };

        AddChild(Label);
    }

    /// <summary>
    ///     Loads gc_pane2.spf and fills the TITLE rect interior with the given semi-transparent color, producing a banner
    ///     texture with a readable text backdrop.
    /// </summary>
    private static Texture2D BuildPanelTexture(Color titleFill)
    {
        var source = UiRenderer.Instance!.GetSpfTexture("gc_pane2.spf");

        if ((source.Width < PANEL_WIDTH) || (source.Height < PANEL_HEIGHT))
            return source;

        using var scope = new PixelBufferScope(source);
        ImageUtil.FillRect(scope.Pixels, scope.Width, scope.Height, TITLE_X, TITLE_Y, TITLE_WIDTH, TITLE_HEIGHT, titleFill);

        var tex = new Texture2D(ChaosGame.Device, scope.Width, scope.Height);
        scope.CommitTo(tex);
        return tex;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        if (!PanelTexturesInitialized)
        {
            PanelTexturesInitialized = true;
            NormalPanelTexture = BuildPanelTexture(NormalFillColor);
            HoverPanelTexture = BuildPanelTexture(HoverFillColor);
        }

        //draw the panel frame — swap texture based on hover state
        var panel = IsHovered ? HoverPanelTexture : NormalPanelTexture;

        if (panel is not null && !panel.IsDisposed)
            DrawTexture(spriteBatch, panel, new Vector2(ScreenX, ScreenY), Color.White);

        //draw children (label)
        base.Draw(spriteBatch);
    }

    /// <summary>
    ///     Updates the displayed text.
    /// </summary>
    public void UpdateText(string text) => Label.Text = text;
}