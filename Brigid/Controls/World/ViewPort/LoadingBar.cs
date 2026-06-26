#region
using Brigid.Controls.Components;
#endregion

namespace Brigid.Controls.World.ViewPort;

/// <summary>
///     Loading/progress screen using _nload prefab. Displays a background with an animated progress bar
///     composed of _nloadb0.spf (start cap), _nloadb1.spf (fill), _nloadb2.spf (end cap).
/// </summary>
public class LoadingBar : PrefabPanel
{
    private readonly UIImage? EndCap;
    private readonly UIProgressBar? FillBar;

    public LoadingBar()
        : base("_nload")
    {
        Name = "Loading";
        Visible = false;

        var cache = UiRenderer.Instance!;
        var startTexture = cache.GetSpfTexture("_nloadb0.spf");
        var fillTexture = cache.GetSpfTexture("_nloadb1.spf");
        var endTexture = cache.GetSpfTexture("_nloadb2.spf");

        var barY = Height - startTexture.Height - 20;
        var barX = 20;

        AddChild(
            new UIImage
            {
                Texture = startTexture,
                X = barX,
                Y = barY
            });

        FillBar = new UIProgressBar
        {
            X = barX + startTexture.Width,
            Y = barY,
            Width = fillTexture.Width,
            Height = fillTexture.Height,
            FillTexture = fillTexture
        };

        AddChild(FillBar);

        EndCap = new UIImage
        {
            Texture = endTexture,
            X = barX + startTexture.Width + fillTexture.Width,
            Y = barY,
            Visible = false
        };

        AddChild(EndCap);
    }

    public void SetProgress(float progress)
    {
        progress = Math.Clamp(progress, 0f, 1f);

        FillBar?.Percent = progress;

        EndCap?.Visible = progress >= 1f;
    }

    public void Show(float initialProgress = 0f)
    {
        SetProgress(initialProgress);
        Visible = true;
    }
}