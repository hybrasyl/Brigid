#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.World.ViewPort;

public sealed class HealthBar : UIElement
{
    private const int TOTAL_WIDTH = 27;
    private const int TOTAL_HEIGHT = 5;
    private const int INNER_WIDTH = TOTAL_WIDTH - 2;
    private const int INNER_HEIGHT = TOTAL_HEIGHT - 2;
    private const float DURATION_MS = 2000f;

    private static readonly Color FrameColor = Color.Black;
    private static readonly Color HighColor = new(0, 97, 0);
    private static readonly Color MidColor = new(247, 142, 24);
    private static readonly Color LowColor = new(206, 0, 16);

    private float ElapsedMs;
    public byte HealthPercent { get; set; }

    public uint EntityId { get; }
    public bool IsExpired => ElapsedMs >= DURATION_MS;

    public HealthBar(uint entityId, byte healthPercent)
    {
        EntityId = entityId;
        HealthPercent = healthPercent;
        Width = TOTAL_WIDTH;
        Height = TOTAL_HEIGHT;
    }

    public override void Draw(SpriteBatch spriteBatch)
    {
        if (!Visible)
            return;

        UpdateClipRect();

        if ((ClipRect.Width <= 0) || (ClipRect.Height <= 0))
            return;

        var innerX = ScreenX + 1;
        var innerY = ScreenY + 1;
        var fillWidth = (int)(INNER_WIDTH * (HealthPercent / 100f));

        //border only — unfilled area is transparent
        DrawBorder(
            spriteBatch,
            new Rectangle(
                ScreenX,
                ScreenY,
                TOTAL_WIDTH,
                TOTAL_HEIGHT),
            FrameColor);

        //fill
        if (fillWidth > 0)
        {
            var fillColor = HealthPercent switch
            {
                > 52 => HighColor,
                > 24 => MidColor,
                _    => LowColor
            };

            DrawRectClipped(
                spriteBatch,
                new Rectangle(
                    innerX,
                    innerY,
                    fillWidth,
                    INNER_HEIGHT),
                fillColor);
        }
    }

    public void Reset(byte healthPercent)
    {
        HealthPercent = healthPercent;
        ElapsedMs = 0;
    }

    public override void Update(GameTime gameTime) => ElapsedMs += (float)gameTime.ElapsedGameTime.TotalMilliseconds;
}