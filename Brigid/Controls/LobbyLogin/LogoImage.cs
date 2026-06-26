#region
using Brigid.Controls.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
#endregion

namespace Brigid.Controls.LobbyLogin;

public sealed class LogoImage : UIImage
{
    private int FrameDirection = 1;
    private double FrameTimer;
    public int CurrentFrame { get; private set; }
    public int FrameIntervalMs { get; set; } = 100;
    public Texture2D[] Frames { get; init; } = [];
    public bool Looping { get; set; } = true;
    public bool PingPong { get; set; }

    public override void Dispose()
    {
        //null out Texture so base.Dispose doesn't dispose a frame we already own and free below
        Texture = null;

        foreach (var frame in Frames)
            frame.Dispose();

        base.Dispose();
    }

    public override void Update(GameTime gameTime)
    {
        if (Frames.Length == 0)
            return;

        Texture = Frames[CurrentFrame];

        if (Frames.Length == 1)
            return;

        FrameTimer += gameTime.ElapsedGameTime.TotalMilliseconds;

        if (FrameTimer < FrameIntervalMs)
            return;

        FrameTimer -= FrameIntervalMs;

        var nextFrame = CurrentFrame + FrameDirection;

        if (nextFrame >= Frames.Length)
        {
            if (PingPong)
            {
                FrameDirection = -1;
                nextFrame = CurrentFrame + FrameDirection;
            } else if (Looping)
                nextFrame = 0;
            else
                return;
        } else if (nextFrame < 0)
        {
            FrameDirection = 1;
            nextFrame = CurrentFrame + FrameDirection;
        }

        CurrentFrame = nextFrame;
    }
}
